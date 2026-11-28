# Postgres Persistence, Live Viewing and Auto Pilot: Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Games are stored in PostgreSQL and survive both a page refresh and an API restart. Viewers follow a game live by scanning a QR code, with updates pushed over SSE. The dealer can run auto pilot with a countdown that dealer and viewers both see.

**Architecture:**
- **API:** ASP.NET Core 8 with EF Core and Npgsql. Every write to a game runs in one transaction that locks the game row and ends with `pg_notify('game_events', gameId)`.
- **Live updates:** each API instance has one listener that `LISTEN`s and wakes in-process SSE subscribers. Each subscriber reloads a full snapshot and sends it.
- **UI:** the static UI stays as plain scripts that work both as browser globals and as CommonJS modules. Pure logic goes in `live.js` and `autopilot.js` so it can be unit and mutation tested.

**Tech Stack:**
- .NET 8 (`net8.0`, built with SDK 10 using roll-forward) and EF Core 8.
- `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.x, Postgres 17.
- Tests: xUnit, `Microsoft.AspNetCore.Mvc.Testing` 8.0.*, `Testcontainers.PostgreSql` 4.x, `Microsoft.Extensions.TimeProvider.Testing` 8.x, `coverlet.msbuild`, `dotnet-stryker`.
- UI: Node `node:test`, c8, Stryker JS, Playwright.
- QR codes: `qrcode-generator` 1.4.4 (MIT), copied into the repo.

**Spec:** `docs/superpowers/specs/2026-11-27-live-viewing-postgres-design.md`

## Global Constraints
- The existing endpoints `GET /GetGameKey`, `GET /Get?GameKey=` and `POST /Reset?GameKey=` keep their request and response shapes. `/Get` at 75 balls still returns `"Game Over!"`.
- The GameKey is the dealer's secret and must never appear in a viewer response or on a viewer page.
- The dealer URL is `/?game=<GameKey>`. The QR encodes `<origin><dir>/watch.html?join=<JoinCode>`. The viewer URL is `watch.html?v=<ViewerId>`.
- Reset keeps the same GameKey and JoinCode, deletes the draws, adds 1 to `Round` and turns auto pilot off. Open SSE streams stay open.
- Auto pilot interval comes from config `AutoPilot:IntervalSeconds`, default **10**. Heartbeat comes from `Live:HeartbeatSeconds`, default **15**. A viewer counts as watching while `LastSeenAt` is within **2× the heartbeat**.
- Viewers see "Waiting for dealer…" when the countdown is **more than 5s overdue**. The countdown reads `Next ball in {n}s`.
- Every SSE message is `event: state` followed by one JSON snapshot, in camelCase. Heartbeats are `: ping`. The response includes the header `X-Accel-Buffering: no`.
- Coverage must be at least **90%** for both .NET and JS. Mutation score break threshold is **80**.
- Per the user's rules, the only existing tests that may be edited are `UI/tests/unit/ngrok-policy.test.js` (the exact-JSON test) and `UI/tests/e2e/api-host.spec.js` (the draw-call list), and only as described in Tasks 8 and 10. No other existing test may be deleted or changed.
- The existing UI code is plain ES2017 browser scripts with no bundler. New modules use the same UMD pattern as `board.js`.

## Review Focus
- **Dealer opened on `localhost`:** phones can't reach a QR that points at localhost, so the dealer page must show the hint to open it through the LAN or ngrok address. The test is in Task 8.
- **Unknown or reset-away join and viewer IDs:** `watch.html` shows "Game not found" instead of a blank board, and the API returns 404. The tests are in Tasks 6 and 9.
- **API restart or dropped stream while watching:** the viewer shows "Reconnecting…", then "Live" again, and catches up from the snapshot sent on connect. The test is in Task 9.
- **SSE and `/State` both deliver the same snapshot:** auto pilot must schedule exactly one draw and never draw twice. The test is in Task 7.
- **A malformed `?game=` value** (for example `?game=abc`) is ignored and a new game starts. It must not cause an API error alert. The tests are in Tasks 7 and 8.

---

## File Structure

```
src/BingoBongo/
  Data/BingoDbContext.cs          DbContext + model config
  Data/Entities.cs                GameEntity, DrawEntity, ViewerEntity
  Data/Migrations/*               EF migration (generated)
  Games/BallPicker.cs             pure balanced draw (moved out of controller)
  Games/AutoPilotSchedule.cs      pure next-draw rule
  Games/GameSnapshot.cs           snapshot records
  Games/GameStore.cs              all DB reads/writes + notify
  Games/Options.cs                AutoPilotOptions, LiveOptions
  Live/GameEventHub.cs            in-process per-game fan-out
  Live/PgNotificationListener.cs  BackgroundService: LISTEN game_events
  Live/SseStream.cs               writes snapshots/heartbeats to a response
  Controllers/BingoController.cs  existing endpoints + State/Events/AutoPilot
  Controllers/ViewerController.cs viewers + watch endpoints
src/BingoBongo.Tests/
  PostgresFixture.cs              [ModuleInitializer] Testcontainer + migrate
  BingoApiFactory.cs              WebApplicationFactory with fast intervals
  SseClient.cs                    test helper: read `event: state` frames
  BallPickerTests.cs, AutoPilotScheduleTests.cs, GameEventHubTests.cs   (Trait Category=Unit)
  PersistenceTests.cs, LiveEventsTests.cs, AutoPilotTests.cs, ViewerTests.cs
  stryker-config.json, .config/dotnet-tools.json (at src/)
src/UI/
  live.js, autopilot.js, watch.html, vendor/qrcode.js
  scripts/integration-api.sh      starts Postgres container + API for Playwright
  tests/unit/live.test.js, tests/unit/autopilot.test.js
  tests/e2e/dealer.spec.js, tests/e2e/watch.spec.js
  tests/integration/live.spec.js
  tests/integration/global-teardown.js
```

---

### Task 1: Postgres groundwork (schema, startup migration, test container, compose)

**Files:**
- Modify: `src/BingoBongo/BingoBongo.csproj`, `src/BingoBongo/Program.cs`, `src/BingoBongo/appsettings.json`, `src/BingoBongo/appsettings.Development.json`, `src/compose.yaml`, `src/.env.example`, `src/BingoBongo.Tests/BingoBongo.Tests.csproj`, `src/UI/package.json`
- Create: `src/BingoBongo/Data/Entities.cs`, `src/BingoBongo/Data/BingoDbContext.cs`, `src/BingoBongo/Data/Migrations/*`, `src/BingoBongo.Tests/PostgresFixture.cs`, `src/BingoBongo.Tests/BingoApiFactory.cs`, `src/BingoBongo.Tests/PersistenceTests.cs`

**Interfaces:**
- Produces:
  - `BingoBongo.Data.BingoDbContext`, with `DbSet<GameEntity> Games`, `DbSet<DrawEntity> Draws` and `DbSet<ViewerEntity> Viewers`.
  - `GameEntity { Guid Id; Guid JoinCode; int Round; DateTimeOffset? AutoPilotNextDrawAt; DateTimeOffset CreatedAt; DateTimeOffset UpdatedAt }`. Table `games`, unique index on `JoinCode`.
  - `DrawEntity { Guid GameId; int Seq; string Ball }`. Table `draws`, primary key `(GameId, Seq)`, unique `(GameId, Ball)`, FK to games with cascade delete.
  - `ViewerEntity { Guid Id; Guid GameId; DateTimeOffset CreatedAt; DateTimeOffset? LastSeenAt }`. Table `viewers`, index `(GameId, LastSeenAt)`.
  - Use snake_case table and column names, set explicitly in `OnModelCreating`, so the raw SQL in Task 3 can rely on them.
  - Connection string name: `ConnectionStrings:Bingo`.
  - Test side: `PostgresFixture.ConnectionString` (static), and `BingoApiFactory : WebApplicationFactory<Program>`, which sets `AutoPilot:IntervalSeconds=1` and `Live:HeartbeatSeconds=1`.

- [ ] **Step 1: Add the packages.**
  - API: `Npgsql.EntityFrameworkCore.PostgreSQL` 8.0.*, `Microsoft.EntityFrameworkCore.Design` 8.0.*.
  - Tests: `Testcontainers.PostgreSql` 4.*, `Microsoft.Extensions.TimeProvider.Testing` 8.*.
- [ ] **Step 2: Write `PostgresFixture`.**
  - A `[ModuleInitializer]` that starts `postgres:17-alpine` with `PostgreSqlBuilder().Build().StartAsync().GetAwaiter().GetResult()`.
  - It sets `Environment.SetEnvironmentVariable("ConnectionStrings__Bingo", cs)` before any factory is created, so the existing `CorsIntegrationTests` get a database without being edited.
  - Once `BingoDbContext` exists, it also runs `new BingoDbContext(options).Database.Migrate()` once.
  - It registers `AppDomain.ProcessExit` to dispose the container.
- [ ] **Step 3: Write the failing test** `PersistenceTests.Startup_applies_migrations`.
  ```csharp
  using var f = new BingoApiFactory();
  using var scope = f.Services.CreateScope();
  var db = scope.ServiceProvider.GetRequiredService<BingoDbContext>();
  Assert.Empty(await db.Database.GetPendingMigrationsAsync());
  Assert.Equal(0, await db.Games.CountAsync(g => g.Id == Guid.NewGuid()));
  ```
- [ ] **Step 4: Run it and check it fails.** Run `cd src && dotnet test --filter Startup_applies_migrations`. Expected: build failure, because `BingoDbContext` doesn't exist yet.
- [ ] **Step 5: Implement the database layer.**
  - Entities, `BingoDbContext` and `Program.cs` wiring: `AddDbContext<BingoDbContext>(o => o.UseNpgsql(cs))`, plus `TimeProvider.System` as a singleton.
  - After `Build()`, run `Database.Migrate()` in a scope.
  - Generate the migration with `dotnet ef migrations add Initial -o Data/Migrations`, using `DOTNET_ROLL_FORWARD=Major` and `dotnet-ef` 8.x in the tool manifest `src/.config/dotnet-tools.json`.
- [ ] **Step 6: Set up the dev database and compose.**
  - `appsettings.Development.json` gets `ConnectionStrings:Bingo = Host=localhost;Port=55432;Database=bingo;Username=postgres;Password=bingo`.
  - `UI/package.json` gets the script `"db": "docker run -d --name bingo-dev-db -p 55432:5432 -e POSTGRES_PASSWORD=bingo -e POSTGRES_DB=bingo postgres:17-alpine || docker start bingo-dev-db"`.
  - `compose.yaml` gets a `db` service: `postgres:17-alpine` with volume `pgdata:/var/lib/postgresql/data`, `POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:-bingo}`, `POSTGRES_DB: bingo`, a `pg_isready` healthcheck, and the comment that migrations run on API startup, so with several replicas you should run them as a separate step.
  - The `api` service gets `depends_on: db: condition: service_healthy` and the env `ConnectionStrings__Bingo`.
  - `.env.example` gets `POSTGRES_PASSWORD=`.
- [ ] **Step 7: Run all .NET tests and check they pass.** Run `cd src && dotnet test`. Expected: the new test and all existing CORS tests pass.
- [ ] **Step 8: Commit** with the message `feat(api): add Postgres schema with EF Core and test container`.

### Task 2: Move the ball choice into `BallPicker`

**Files:**
- Create: `src/BingoBongo/Games/BallPicker.cs`, `src/BingoBongo.Tests/BallPickerTests.cs`

**Interfaces:**
- Produces: `public static class BallPicker { public static string? Next(IReadOnlyCollection<string> called, Random rand); }`. It returns null once all 75 balls are called. This is the existing algorithm from `BingoController.Index` lines 61–77, unchanged.

- [ ] **Step 1: Write the failing tests** (`[Trait("Category","Unit")]`).
  - `Draws_all_75_distinct_balls_then_null`: loop from an empty set and add each result. Assert the set has 75 entries, every ball matches `^[BINGO](\d+)$` inside its letter's range (B 1–15 … O 61–75), and the 76th call returns `null`.
  - `Letters_stay_within_one_of_each_other` (a Theory with seeds 1, 2, 3): after every draw, `max(countPerLetter) - min(countPerLetter) <= 1`.
  - `Picks_from_letter_with_most_remaining`: called = all B, I, N, G except `G60`, and no O. Repeated calls (20 seeds) always return an O ball.
  - `Never_returns_a_called_ball`: called = 74 balls with only `N42` missing, so the result must be `"N42"`.
- [ ] **Step 2: Run them and check they fail.** Run `dotnet test --filter Category=Unit`. Expected: compile error, because `BallPicker` is missing.
- [ ] **Step 3: Implement `BallPicker.Next`**, moving the LINQ from the controller as-is. The controller still calls it for now.
- [ ] **Step 4: Run them and check they pass.** Run `dotnet test`. Expected: all tests pass.
- [ ] **Step 5: Commit** with the message `refactor(api): move the balanced draw into BallPicker`.

### Task 3: `GameStore` replaces `IMemoryCache` (draw, reset, persistence, concurrency)

**Files:**
- Create: `src/BingoBongo/Games/GameSnapshot.cs`, `src/BingoBongo/Games/Options.cs`, `src/BingoBongo/Games/GameStore.cs`
- Modify: `src/BingoBongo/Controllers/BingoController.cs`, `src/BingoBongo/Program.cs` (remove `AddMemoryCache`, register `GameStore` as scoped, bind the options)
- Test: `src/BingoBongo.Tests/PersistenceTests.cs`

**Interfaces:**
- Consumes: `BingoDbContext` (Task 1), `BallPicker.Next` (Task 2).
- Produces:
  - `record AutoPilotState(bool On, DateTimeOffset? NextDrawAt)`
  - `record GameSnapshot(int Round, int NumberOfDrew, string? CurrentNumber, IReadOnlyDictionary<int,string> History, AutoPilotState AutoPilot, DateTimeOffset ServerTime, int ViewerCount, Guid? JoinCode)`. `History` keys are 0-based draw order, as in the existing `Dictionary<int,string>`. `JoinCode` is null for viewers.
  - `record DrawResult(bool GameOver, string? Ball, IReadOnlyDictionary<int,string> History)`
  - `class AutoPilotOptions { int IntervalSeconds = 10; }` bound to section `AutoPilot`.
  - `class LiveOptions { int HeartbeatSeconds = 15; }` bound to section `Live`.
  - `GameStore(BingoDbContext db, TimeProvider clock, IOptions<AutoPilotOptions> autoPilot, IOptions<LiveOptions> live)`, with:
    - `Task<DrawResult> DrawAsync(Guid gameKey, CancellationToken ct)`
    - `Task ResetAsync(Guid gameKey, CancellationToken ct)`
    - `Task<GameSnapshot> GetDealerSnapshotAsync(Guid gameKey, CancellationToken ct)`, which creates the game if it's missing.
    - `Task SetAutoPilotAsync(Guid gameKey, bool on, CancellationToken ct)` is added in Task 5.
    - Viewer methods are added in Task 6.

- [ ] **Step 1: Write the failing integration tests.**
  - `Game_survives_a_new_api_instance`: draw 3 balls with `GET /Get?GameKey=k` on factory A. Dispose A and create factory B. On B, `GET /State?GameKey=k` returns `numberOfDrew == 3` with the same history values in the same order.
  - `Concurrent_draws_never_duplicate`: 20 parallel `GET /Get?GameKey=k`, all 200. The `currentNumber` values are 20 distinct balls, and `/State` reports `numberOfDrew == 20`.
  - `Reset_clears_draws_keeps_join_code_and_bumps_round`: draw 2 and read `joinCode` and `round` from `/State`. `POST /Reset`. Now `/State` has `numberOfDrew == 0`, the same `joinCode`, and `round == before + 1`.
  - `Seventy_sixth_draw_is_game_over`: 75 draws, then the 76th returns the body `"Game Over!"`.
  - `Get_response_shape_is_unchanged`: the JSON has `numberOfDrew`, `currentNumber`, `history`, `orderedDrawHistory` and `orderedBingo`.
- [ ] **Step 2: Run them and check they fail.** Run `dotnet test --filter FullyQualifiedName~PersistenceTests`. Expected: `/State` returns 404, so the tests fail.
- [ ] **Step 3: Implement `GameStore`** and switch the controller over. Add `GET /State?GameKey=` (400 for an empty GUID), which returns the dealer snapshot.
  - **Ensure the game row exists:** `INSERT INTO games (id, join_code, round, created_at, updated_at) VALUES (…) ON CONFLICT (id) DO NOTHING`. The join code is `Guid.NewGuid()`.
  - **Lock it:** `SELECT … FROM games WHERE id = @id FOR UPDATE` through `FromSqlInterpolated`, inside `BeginTransactionAsync`. This is the per-game lock that makes concurrent draws safe.
  - **Draw:** load the called balls for the game, call `BallPicker.Next`, then insert `DrawEntity { Seq = count }`.
  - **Reset:** after taking the lock, delete the draws, add 1 to `Round`, and set `AutoPilotNextDrawAt = null`.
  - Every write calls the private `NotifyAsync(gameId)`, which runs `SELECT pg_notify('game_events', {0})`, just before commit. Postgres delivers notifications on commit.
  - `ViewerCount` is the count of viewers where `LastSeenAt > now - 2 × HeartbeatSeconds`.
- [ ] **Step 4: Run them and check they pass.** Run `dotnet test`. Expected: all tests pass, including CORS.
- [ ] **Step 5: Commit** with the message `feat(api): persist games in Postgres with locked, notified draws`.

### Task 4: Live updates (event hub, LISTEN/NOTIFY, SSE `/Events`, nginx)

**Files:**
- Create: `src/BingoBongo/Live/GameEventHub.cs`, `src/BingoBongo/Live/PgNotificationListener.cs`, `src/BingoBongo/Live/SseStream.cs`, `src/BingoBongo.Tests/GameEventHubTests.cs`, `src/BingoBongo.Tests/SseClient.cs`, `src/BingoBongo.Tests/LiveEventsTests.cs`
- Modify: `src/BingoBongo/Controllers/BingoController.cs`, `src/BingoBongo/Program.cs`, `src/UI/nginx.conf`

**Interfaces:**
- Consumes: the `GameStore` snapshot methods (Task 3).
- Produces:
  - `GameEventHub` (singleton), with:
    - `GameSubscription Subscribe(Guid gameId)`. `GameSubscription : IDisposable` exposes `ChannelReader<bool> Changes`. It uses a bounded channel with capacity 1 and `DropOldest`, so a burst of changes collapses into one reload.
    - `void Publish(Guid gameId)`
    - `void PublishAll()`, used after the listener reconnects.
  - `PgNotificationListener : BackgroundService`, with a dedicated `NpgsqlConnection`:
    - It runs `LISTEN game_events`, and `Notification` events become `hub.Publish(Guid.Parse(payload))`.
    - The loop is `await conn.WaitAsync(ct)`. On an exception it waits 1s, reconnects and calls `PublishAll()`.
  - `static class SseStream`, with `Task RunAsync(HttpContext ctx, Guid gameId, Func<CancellationToken, Task<GameSnapshot?>> load, Func<SsePresence, CancellationToken, Task>? presence, GameEventHub hub, TimeSpan heartbeat)`:
    - It sets `Content-Type: text/event-stream`, `Cache-Control: no-cache` and `X-Accel-Buffering: no`.
    - It subscribes, then writes the snapshot.
    - It then loops on either a hub change (write the snapshot) or the heartbeat timer (write `: ping\n\n` and call `presence(Heartbeat)`).
    - It calls `presence(Connected)` first and `presence(Disconnected)` in a `finally`.
    - JSON uses `JsonSerializerDefaults.Web`.
    - `enum SsePresence { Connected, Heartbeat, Disconnected }`.
  - `GET /Events?GameKey=` streams dealer snapshots (presence is null).

- [ ] **Step 1: Write the failing unit tests** in `GameEventHubTests` (Trait Unit).
  - `Publish_wakes_only_that_games_subscribers`.
  - `Many_publishes_collapse_into_one_pending_change`: publish 5 times, then `TryRead` succeeds once and then fails.
  - `Disposed_subscription_no_longer_receives`.
  - `PublishAll_wakes_every_subscriber`.
- [ ] **Step 2: Write the failing integration tests** in `LiveEventsTests`. `SseClient.ReadStatesAsync(HttpClient, url)` returns an `IAsyncEnumerable<JsonElement>` that parses the `data:` line after each `event: state`, using `HttpCompletionOption.ResponseHeadersRead`.
  - `Events_sends_snapshot_on_connect`: the first frame has `numberOfDrew == 0`, and the response has the header `X-Accel-Buffering: no`.
  - `Events_pushes_snapshot_after_draw`: with the stream open, `/Get`. The next frame has `numberOfDrew == 1`, and its `currentNumber` matches the draw.
  - `Reset_keeps_stream_open_and_pushes_cleared_board`: draw, then reset. The next frames show the board cleared, and a later draw still arrives on the same stream.
  - `Draw_on_one_instance_reaches_subscriber_on_another`: factories A and B on the same database. Subscribe on B, draw on A, and B gets `numberOfDrew == 1` within 5s.
  - `Events_sends_heartbeat_comment`: with `Live:HeartbeatSeconds=1`, raw text contains `: ping` within 3s.
- [ ] **Step 3: Run them and check they fail.** Run `dotnet test --filter "FullyQualifiedName~GameEventHub|FullyQualifiedName~LiveEvents"`. Expected: they fail to compile, or return 404.
- [ ] **Step 4: Implement** the hub, listener, `SseStream`, the endpoint and the `Program.cs` registrations.
- [ ] **Step 5: Update nginx.** In `nginx.conf` `location /api/`, add `proxy_http_version 1.1; proxy_set_header Connection ''; proxy_buffering off; proxy_read_timeout 1h;`.
- [ ] **Step 6: Run all tests and check they pass.** Run `dotnet test`. Expected: all tests pass.
- [ ] **Step 7: Commit** with the message `feat(api): push game snapshots over SSE via Postgres LISTEN/NOTIFY`.

### Task 5: Auto pilot on the server (schedule rule and `/AutoPilot`)

**Files:**
- Create: `src/BingoBongo/Games/AutoPilotSchedule.cs`, `src/BingoBongo.Tests/AutoPilotScheduleTests.cs`, `src/BingoBongo.Tests/AutoPilotTests.cs`
- Modify: `src/BingoBongo/Games/GameStore.cs`, `src/BingoBongo/Controllers/BingoController.cs`

**Interfaces:**
- Produces:
  - `static class AutoPilotSchedule`:
    - `DateTimeOffset? AfterDraw(DateTimeOffset? current, int drawCount, DateTimeOffset now, TimeSpan interval)` returns null if `current` is null or `drawCount >= 75`, and `now + interval` otherwise.
    - `DateTimeOffset? Toggle(bool on, int drawCount, DateTimeOffset now, TimeSpan interval)` returns null if `!on` or `drawCount >= 75`, and `now + interval` otherwise.
  - `GameStore.SetAutoPilotAsync(Guid gameKey, bool on, CancellationToken ct)`.
  - `POST /AutoPilot?GameKey=&on=` returns 204, or 400 for an empty key.

- [ ] **Step 1: Write the failing unit tests** (Trait Unit), using times built from `DateTimeOffset.UnixEpoch`.
  - `AfterDraw_off_stays_off`.
  - `AfterDraw_on_moves_to_now_plus_interval`: the result is `now + 10s`.
  - `AfterDraw_at_75_turns_off`.
  - `AfterDraw_at_74_stays_on`.
  - `Toggle_on_with_no_draws_schedules`.
  - `Toggle_off_clears`.
  - `Toggle_on_when_game_over_stays_off`.
- [ ] **Step 2: Write the failing integration tests** (`BingoApiFactory` sets the interval to 1s; use `FakeTimeProvider` through `WithWebHostBuilder` → `services.AddSingleton<TimeProvider>(fake)`).
  - `AutoPilot_on_sets_next_draw_at_and_pushes_snapshot`: after `/AutoPilot?on=true`, `/State` has `autoPilot.on == true` and `nextDrawAt == fake.now + 1s`, and the open SSE stream receives the same snapshot.
  - `Draw_moves_next_draw_at_forward`: advance the fake clock 5s, draw, and `nextDrawAt == now + 1s`.
  - `AutoPilot_off_clears`.
  - `Reset_turns_auto_pilot_off`.
  - `Seventy_fifth_draw_turns_auto_pilot_off`.
  - `Snapshot_server_time_comes_from_clock`: `serverTime == fake.GetUtcNow()`.
- [ ] **Step 3: Run them and check they fail.** Run `dotnet test --filter "FullyQualifiedName~AutoPilot"`. Expected: they fail.
- [ ] **Step 4: Implement.** `DrawAsync` sets `AutoPilotNextDrawAt = AutoPilotSchedule.AfterDraw(…)` in the same transaction. `SetAutoPilotAsync` uses `Toggle` and calls `NotifyAsync`.
- [ ] **Step 5: Run all tests and check they pass.** Run `dotnet test`. Expected: all tests pass.
- [ ] **Step 6: Commit** with the message `feat(api): add auto pilot schedule shared through snapshots`.

### Task 6: Viewers (join, watch state and events, viewer count)

**Files:**
- Create: `src/BingoBongo/Controllers/ViewerController.cs`, `src/BingoBongo.Tests/ViewerTests.cs`
- Modify: `src/BingoBongo/Games/GameStore.cs`

**Interfaces:**
- Produces:
  - `GameStore.JoinAsync(Guid joinCode, CancellationToken ct) -> Task<Guid?>`: a new viewer ID, or null if no game has that JoinCode.
  - `GameStore.GetViewerSnapshotAsync(Guid viewerId, CancellationToken ct) -> Task<(Guid GameId, GameSnapshot Snapshot)?>`, where the snapshot's `JoinCode` is null.
  - `GameStore.UpdatePresenceAsync(Guid viewerId, SsePresence p, CancellationToken ct)`:
    - `Connected`: set `LastSeenAt = now`, then notify.
    - `Heartbeat`: set `LastSeenAt = now`, with no notify.
    - `Disconnected`: set `LastSeenAt = null`, then notify.
  - `POST /viewers?join=` returns 200 `{ "viewerId": "<guid>" }`, or 404.
  - `GET /watch/{viewerId}/state` returns 200 with the snapshot, or 404.
  - `GET /watch/{viewerId}/events` is the SSE stream, or 404 before the stream starts.

- [ ] **Step 1: Write the failing integration tests.**
  - `Each_join_gets_a_distinct_viewer_id`: join 3 times with the same JoinCode, and you get 3 distinct GUIDs.
  - `Unknown_join_code_is_404`.
  - `Unknown_viewer_is_404_for_state_and_events`.
  - `Viewer_state_matches_game_and_hides_dealer_secrets`: after 2 draws, the viewer state has the same history, the raw JSON doesn't contain the GameKey string, and `joinCode` is null.
  - `Viewer_stream_follows_draws_and_reset`.
  - `Viewer_count_rises_on_connect_and_falls_on_disconnect`: with the dealer stream open, opening a viewer stream makes a dealer frame arrive with `viewerCount == 1`. Cancelling the viewer stream makes a dealer frame arrive with `viewerCount == 0`.
  - `Viewer_id_used_as_game_key_cannot_touch_real_game`: `GET /Get?GameKey=<viewerId>` doesn't change the real game's `numberOfDrew`.
  - `Stale_viewer_is_not_counted`: set `LastSeenAt` to now − 3 × heartbeat directly in the database, and `viewerCount == 0`.
- [ ] **Step 2: Run them and check they fail.** Run `dotnet test --filter FullyQualifiedName~ViewerTests`. Expected: 404 for the new routes.
- [ ] **Step 3: Implement** `ViewerController` and the store methods. The events endpoint calls `SseStream.RunAsync` with `presence = (p, ct) => store.UpdatePresenceAsync(viewerId, p, ct)`, using a fresh DI scope for each call, because the request scope's DbContext isn't thread-safe across the loop.
- [ ] **Step 4: Run all tests and check they pass.** Run `dotnet test`. Expected: all tests pass.
- [ ] **Step 5: Commit** with the message `feat(api): add viewer join, watch stream and live viewer count`.

### Task 7: UI logic modules (`live.js`, `autopilot.js`)

**Files:**
- Create: `src/UI/live.js`, `src/UI/autopilot.js`, `src/UI/tests/unit/live.test.js`, `src/UI/tests/unit/autopilot.test.js`
- Modify: `src/UI/package.json` (`test:coverage` gets `--include live.js --include autopilot.js`), `src/UI/stryker.config.json` (`mutate` gets `live.js` and `autopilot.js`)

**Interfaces:**
- Produces `BingoLive` (a browser global, or `module.exports` in Node):
  - `gameKeyFromUrl(search) -> string|null`, which accepts only a GUID matching `/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i`.
  - `joinCodeFromUrl(search)` and `viewerIdFromUrl(search)`, which read `join` and `v` with the same GUID check.
  - `withParam(href, name, value) -> string`, which keeps the other parameters.
  - `joinUrl(location, joinCode) -> string`, built from `location.origin` plus the directory of `location.pathname` plus `watch.html?join=<code>`.
  - `isLocalOnly(hostname) -> boolean`, true for `localhost`, `127.*` and `::1` / `[::1]`.
  - `clockOffset(serverTimeIso, nowMs) -> number` in milliseconds.
  - `countdownText(snapshot, nowMs, offsetMs) -> string`:
    - `''` when auto pilot is off.
    - `'Next ball in {ceil(s)}s'` while time remains.
    - `'Drawing…'` when 0–5s overdue.
    - `'Waiting for dealer…'` when more than 5s overdue.
  - `statusText(readyState) -> 'Live' | 'Reconnecting…'`, where `EventSource.OPEN` (1) gives `'Live'`.
  - `viewState(snapshot, placeholder) -> { currentNumber, history }`, where `history` is `Object.values(snapshot.history)` in key order and `currentNumber ?? placeholder`.
- Produces `BingoAutoPilot.create({ draw, now, setTimer, clearTimer })` returning `{ sync(snapshot, offsetMs), stop() }`:
  - `sync` with auto pilot off clears the timer.
  - With auto pilot on, it schedules one timer for `nextDrawAt − (now() + offset)`, clamped to 0 or more.
  - When the same `nextDrawAt` is already scheduled, it does nothing.
  - When the timer fires it calls `draw()` once and ignores further fires until that promise settles.

- [ ] **Step 1: Write the failing tests** with `node:test`.
  - `live.test.js` has one test per function above, including:
    - `gameKeyFromUrl('?game=abc') === null`.
    - `joinUrl({origin:'https://x.ngrok.app', pathname:'/'}, J) === 'https://x.ngrok.app/watch.html?join=' + J`, and with pathname `/bingo/index.html` the result contains `/bingo/watch.html`.
    - countdown boundaries at +3.2s (`'Next ball in 4s'`), −1s (`'Drawing…'`), −5s (`'Drawing…'`) and −5.001s (`'Waiting for dealer…'`).
    - A test that the offset is applied.
  - `autopilot.test.js` uses a fake timer list:
    - `schedules one draw at nextDrawAt`.
    - `same snapshot twice schedules once`: two `sync` calls give one timer and one `draw` call.
    - `new nextDrawAt reschedules`: the old timer is cleared.
    - `off clears timer`.
    - `overdue fires immediately`: delay 0.
    - `does not draw twice while a draw is in flight`.
    - `stop clears timer`.
- [ ] **Step 2: Run them and check they fail.** Run `cd src/UI && npm test`. Expected: "Cannot find module '../../live.js'".
- [ ] **Step 3: Implement both modules** with the UMD pattern from `board.js`.
- [ ] **Step 4: Run coverage and check it passes.** Run `npm run test:coverage`. Expected: all tests pass, with at least 90% lines, branches, functions and statements.
- [ ] **Step 5: Run mutation testing and check it passes.** Run `npm run test:mutation`. Expected: a score of at least 80, and kill any surviving mutants that matter.
- [ ] **Step 6: Commit** with the message `feat(ui): add live-view and auto pilot logic modules`.

### Task 8: Dealer page (URL key, refresh, SSE, QR, viewer count, auto pilot, countdown)

**Files:**
- Create: `src/UI/vendor/qrcode.js` (`qrcode-generator` 1.4.4 `qrcode.js` with its MIT header kept), `src/UI/tests/e2e/dealer.spec.js`
- Modify: `src/UI/index.html`, `src/UI/tests/e2e/api-host.spec.js` (approved change only)

**Interfaces:**
- Consumes: `BingoLive` and `BingoAutoPilot` (Task 7). The API endpoints from Tasks 3–5.
- **DOM contract** that the tests rely on:
  - `#autoPilotBtn`, with `aria-pressed` and the text `▶ Auto pilot` / `■ Stop`.
  - `#countdown`, which holds the `countdownText`.
  - `#qr`, with `data-url` set to the join URL and the SVG inside.
  - `#joinLink`, a link to the join URL.
  - `#viewerCount`, with the text `👁 {n} watching`.
  - `#localHint`, visible only when `isLocalOnly(location.hostname)`.

**Behaviour:**
- **On load:**
  - If `gameKeyFromUrl` finds a key, `GET /State` and render, then open `EventSource(API + '/Events?GameKey=')`.
  - Otherwise, keep today's lazy flow.
- **First draw:** after `/GetGameKey`, call `history.replaceState(null, '', withParam(location.href, 'game', key))`, then open SSE.
- **Every snapshot** (from `/State` or SSE) goes through one `applySnapshot(s)`:
  - Render through `viewState(s, 'Push')`, show the QR, update the viewer count, recompute the clock offset, and call `autopilot.sync(s, offset)`.
  - Update `#countdown` on a 250ms interval.
- **Draw sound:** keep `playDrawSound()` in `drawNext`. Auto pilot's `draw` is `drawNext`, followed by `GET /State` → `applySnapshot`.
- **Auto pilot button:** the button calls `POST /AutoPilot?on=` and then applies `/State`.
- **Errors:** errors in the new calls are logged with `console.warn`, never shown with `alert`.

- [ ] **Step 1: Write the failing e2e tests** in `dealer.spec.js`. Mock routes the way `board.spec.js` does, and add `/State`, `/AutoPilot` and an `/Events` route that fulfils `text/event-stream` with one state frame.
  - `first draw puts the game key in the URL`: after the click, `expect(page).toHaveURL(/\?game=1111…/)`.
  - `refresh restores the board without drawing`: goto `/?game=K` with `/State` mocked to 3 draws. There are 3 `td.cell.called`, and zero requests hit `/Get`.
  - `malformed game param starts fresh`: goto `/?game=abc`. There's no `/State` request and no dialog.
  - `shows QR with the join link and viewer count`: `#qr` has `data-url` equal to `http://localhost:8080/watch.html?join=<J>`, and `#viewerCount` has the text `👁 2 watching`.
  - `local hint shows on localhost`: `#localHint` is visible.
  - `auto pilot toggles and counts down`, using `page.clock.install()`:
    - Click `#autoPilotBtn` and assert the POST went to `/AutoPilot?…on=true`.
    - With the mocked snapshot `nextDrawAt = now + 10s`, `#countdown` reads `Next ball in 10s`.
    - `clock.runFor(10_000)` sends one `/Get`.
    - Clicking again shows `▶ Auto pilot` and sends `on=false`.
- [ ] **Step 2: Update `api-host.spec.js`** (approved). In "draws call the API on the host…", keep the host assertion but compare only the draw calls: `expect(calls.filter(c => /\/(GetGameKey|Get)$/.test(c))).toEqual(['127.0.0.1:5001/GetGameKey','127.0.0.1:5001/Get'])`.
- [ ] **Step 3: Run them and check they fail.** Run `npm run test:e2e`. Expected: the new dealer tests fail and the existing ones pass.
- [ ] **Step 4: Implement the `index.html` changes and copy in `vendor/qrcode.js`.**
  - Load order: `board.js`, `config.js`, `api.js`, `live.js`, `autopilot.js`, `vendor/qrcode.js`.
  - The QR is made with `qrcode(0,'M')`, `.addData(url)`, `.make()`, `.createSvgTag({cellSize:4, margin:2})`.
  - On phones (`max-width:720px`), put the QR card below the board.
- [ ] **Step 5: Run them and check they pass.** Run `npm run test:e2e`. Expected: all tests pass, including every existing `board.spec.js` test.
- [ ] **Step 6: Commit** with the message `feat(ui): dealer gets refresh-safe URL, QR join, viewer count and auto pilot`.

### Task 9: Viewer page (`watch.html`)

**Files:**
- Create: `src/UI/watch.html`, `src/UI/tests/e2e/watch.spec.js`

**Interfaces:**
- Consumes: `BingoBoard`, `BingoLive`, `BingoApi`, and `/viewers` and `/watch/*` (Task 6).
- **DOM contract:**
  - It reuses `#currentNumber`, `#track` and `#boardBody`, with the same markup and CSS as `index.html`. Copy the styles; don't share a stylesheet, because the Dockerfile copies flat files.
  - `#status` reads `Live`, `Reconnecting…` or `Waiting for dealer…`.
  - `#countdown`.
  - `#notFound`, which reads "Game not found".

**Behaviour:**
- With `?join=J`: `POST /viewers?join=J`. On 200, `history.replaceState` to `?v=<id>`. On 404, show `#notFound`.
- With `?v=V`: open `EventSource(API + '/watch/V/events')`.
  - Each `state` event goes through `viewState(s, '--')`, then render and set the clock offset.
  - `onerror` with `readyState !== OPEN` sets `#status` from `statusText`. `onopen` sets it to `Live`.
  - If the countdown text is `Waiting for dealer…`, `#status` shows that instead.
- If the first connection gets a 404 (checked once with `fetch('/watch/V/state')` before opening SSE), show `#notFound`.
- No `/Get`, `/Reset` or `/AutoPilot` request is ever made.

- [ ] **Step 1: Write the failing e2e tests.**
  - `join swaps to a personal viewer url`: `/viewers` is mocked to return V, and the URL ends with `watch.html?v=V`.
  - `renders snapshot from the event stream`: an SSE mock with 4 draws gives 4 called cells and `#currentNumber` equal to the last draw.
  - `unknown join shows game not found`.
  - `unknown viewer shows game not found`.
  - `shows countdown and waiting for dealer when overdue`, using `page.clock`: `nextDrawAt = now + 3s` shows `Next ball in 3s`. After `runFor(9000)`, `#status` reads `Waiting for dealer…`.
  - `reconnecting then live`: the first `/events` route aborts and the second fulfils. `#status` goes from `Reconnecting…` to `Live`.
  - `viewer never calls dealer endpoints`: record requests, then assert none match `/Get|Reset|AutoPilot|GetGameKey`.
- [ ] **Step 2: Run them and check they fail.** Run `npm run test:e2e -- watch.spec.js`. Expected: 404 for `watch.html`.
- [ ] **Step 3: Implement `watch.html`.**
- [ ] **Step 4: Run them and check they pass.** Run `npm run test:e2e`. Expected: all tests pass.
- [ ] **Step 5: Commit** with the message `feat(ui): add live viewer page`.

### Task 10: Open the viewer paths on ngrok, plus Docker packaging

**Files:**
- Modify: `src/ngrok/render-policy.sh`, `src/UI/tests/unit/ngrok-policy.test.js` (approved change plus new tests), `src/UI/Dockerfile`, `src/UI/tests/docker/stack.spec.js` (new tests only)

**Interfaces:**
- The policy becomes `{"on_http_request":[{"expressions":[EXPR],"actions":[{"type":"basic-auth","config":{"credentials":["u:p"]}}]}]}`.
- `EXPR` is `!(req.url.path in ['/watch.html','/board.js','/api.js','/config.js','/live.js','/api/viewers'] || req.url.path.startsWith('/api/watch/'))`.
- **Before coding, check this expression's syntax** against the ngrok Traffic Policy docs (https://ngrok.com/docs/traffic-policy/ — expressions and `req.url.path`). If the docs differ, use the documented form and update the test to match.

- [ ] **Step 1: Update and extend `ngrok-policy.test.js`.**
  - In "renders a basic-auth traffic policy…", change the expected JSON to include `expressions: [EXPR]` (the approved change).
  - Add `exempts exactly the viewer paths`: parse the expression's path list and assert it equals the 6 paths above, and that `/api/watch/` is the only prefix.
  - Add `does not exempt dealer paths`: the expression's text doesn't contain `/index.html`, `'/'`, `/api/Get`, `/api/Reset`, `/api/State`, `/api/Events` or `/api/AutoPilot` in the exempt list.
- [ ] **Step 2: Run them and check they fail.** Run `npm test`. Expected: the policy tests fail.
- [ ] **Step 3: Implement.**
  - `render-policy.sh` prints the new JSON. The single quotes in EXPR are fine, because no JSON escaping is needed for them.
  - `UI/Dockerfile`: `COPY index.html watch.html board.js api.js live.js autopilot.js /usr/share/nginx/html/` and `COPY vendor /usr/share/nginx/html/vendor`.
  - Add `/vendor/qrcode.js` to the exempt list **only if** `watch.html` loads it. It doesn't, so leave it out.
- [ ] **Step 4: Add the Docker stack tests** in `stack.spec.js`, appending new tests only.
  - `game survives an api restart`:
    - Get a key via `/api/GetGameKey` and draw twice via `/api/Get`.
    - Run `execSync('docker compose restart api', { cwd: '..' })`.
    - Poll `/api/State?GameKey=` until it returns 200, and assert `numberOfDrew == 2`.
  - `watch page streams through nginx`:
    - Join through `/api/viewers?join=` (the join code comes from `/api/State`).
    - Open `watch.html?v=…` in a page, draw through the API, and the page shows the drawn ball within 3s. This proves nginx isn't buffering.
  - `ngrok: viewer paths need no auth but dealer page does`. Skip it when there's no tunnel, as the existing ngrok tests do. `/watch.html` returns 200 without auth, while `/` and `/api/GetGameKey` return 401.
- [ ] **Step 5: Run them and check they pass.**
  - Run `npm test`. Expected: all unit tests pass.
  - Run `cd src && docker compose up -d --build && cd UI && npm run test:docker`. Expected: all tests pass, with ngrok tests skipped if there's no token.
- [ ] **Step 6: Commit** with the message `feat(infra): open viewer paths on ngrok and package live view in Docker`.

### Task 11: Playwright integration against real Postgres and the real API

**Files:**
- Create: `src/UI/scripts/integration-api.sh`, `src/UI/tests/integration/global-teardown.js`, `src/UI/tests/integration/live.spec.js`
- Modify: `src/UI/playwright.config.js` (the `webServer` command for INTEGRATION, and `globalTeardown`)

**Interfaces:**
- `integration-api.sh`:
  1. `docker run -d --name bingo-it-db -p 55433:5432 -e POSTGRES_PASSWORD=bingo -e POSTGRES_DB=bingo postgres:17-alpine`, or `docker start bingo-it-db` if it exists.
  2. Loop on `docker exec bingo-it-db pg_isready -U postgres` until it's ready.
  3. `exec env ConnectionStrings__Bingo='Host=localhost;Port=55433;Database=bingo;Username=postgres;Password=bingo' AutoPilot__IntervalSeconds=2 Live__HeartbeatSeconds=2 DOTNET_ROLL_FORWARD=Major dotnet run --project ../BingoBongo/BingoBongo.csproj --no-launch-profile --urls http://localhost:5001`.
  - The script starts its own database because Playwright's `webServer` doesn't wait for `globalSetup`.
- `global-teardown.js`: `docker rm -f bingo-it-db`, ignoring errors. It only runs when `INTEGRATION` is set.
- The existing `board.spec.js` and `lan.spec.js` integration tests stay unchanged.

- [ ] **Step 1: Write the failing tests** in `live.spec.js`. Each uses two browser contexts, dealer and viewer.
  - `viewer board follows dealer draws`:
    - The dealer draws once, then reads `#qr[data-url]`.
    - The viewer opens that URL, and its URL changes to `?v=`.
    - The dealer draws 3 more times, and the viewer shows 4 called cells and the same `#currentNumber`.
  - `reset clears viewer board without reload`: the viewer shows 0 called cells, and `page.url()` is unchanged (no navigation).
  - `dealer refresh keeps the game`: after 3 draws, `page.reload()` still shows 3 called cells and the same `?game=`.
  - `viewer count goes up when a viewer joins`: the dealer's `#viewerCount` changes to `👁 1 watching`, and back to `👁 0 watching` after the viewer context closes, within 10s.
  - `auto pilot draws on its own and both screens count down` (interval 2s):
    - Start auto pilot. Both `#countdown` elements match `/Next ball in [12]s/`.
    - Within 7s the viewer shows at least 2 more called cells.
    - Press Stop, and the count stays the same for 5s.
- [ ] **Step 2: Run them and check they fail.** Run `npm run test:integration`. Expected: the new tests fail until the script and config are wired up.
- [ ] **Step 3: Implement** the script and the config change: the `webServer` command becomes `sh scripts/integration-api.sh`, and the URL and timeout stay the same.
- [ ] **Step 4: Run them and check they pass.** Run `npm run test:integration`. Expected: all tests pass, including the existing `board.spec.js`.
- [ ] **Step 5: Commit** with the message `test(ui): add live view, refresh and auto pilot tests against the real stack`.

### Task 12: .NET coverage and mutation gates, README, final verification

**Files:**
- Modify: `src/BingoBongo.Tests/BingoBongo.Tests.csproj` (add `coverlet.msbuild` 6.0.*), `src/.config/dotnet-tools.json` (add `dotnet-stryker` 4.*), `README.md` (or create `src/README.md` if there's no root README; there's currently none, so create `README.md` at the repo root)
- Create: `src/BingoBongo.Tests/stryker-config.json`

- [ ] **Step 1: Turn on the coverage gate.**
  - Run `cd src && dotnet test /p:CollectCoverage=true /p:Threshold=90 /p:ThresholdType=line%2cbranch /p:ExcludeByFile="**/Migrations/*.cs%2c**/WeatherForecast.cs"`.
  - Expected: it passes at 90% or more. If it doesn't, add tests for the uncovered branches the report names, such as listener reconnect or 400s for empty keys.
- [ ] **Step 2: Configure and run mutation testing.**
  - `stryker-config.json`: `{"stryker-config":{"project":"BingoBongo.csproj","mutate":["**/Games/BallPicker.cs","**/Games/AutoPilotSchedule.cs","**/Live/GameEventHub.cs"],"test-case-filter":"Category=Unit","thresholds":{"high":90,"low":80,"break":80}}}`.
  - Run `cd src/BingoBongo.Tests && DOTNET_ROLL_FORWARD=Major dotnet stryker`.
  - Expected: a mutation score of at least 80. Kill the surviving mutants with more unit tests.
- [ ] **Step 3: Write the README.** It covers:
  - Running locally: `npm run db`, `npm run api`, `npm run serve`.
  - Docker with Postgres.
  - How viewing works: QR → join → personal viewer link.
  - Auto pilot.
  - The test commands.
  - The note on running migrations separately when you have several replicas.
- [ ] **Step 4: Full verification.** Run all of these and paste the results:
  - `cd src && dotnet test`
  - `cd UI && npm run test:coverage`
  - `npm run test:mutation`
  - `npm run test:e2e`
  - `npm run test:integration`
  - `cd .. && docker compose up -d --build && cd UI && npm run test:docker`
  - Expected: everything is green, and both coverage gates are at 90% or more.
- [ ] **Step 5: Commit** with the message `chore: gate .NET coverage and mutation score; document live viewing`.
