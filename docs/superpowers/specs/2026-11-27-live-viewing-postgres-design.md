# Bingo: Postgres persistence, refresh-safe URLs, live viewing (QR + SSE), auto pilot

## Context
Today every game lives in `IMemoryCache` inside one API process (`src/BingoBongo/Controllers/BingoController.cs`). In the browser, `gameKey` is only a JS variable (`src/UI/index.html:160`). That causes four problems:
- A page refresh loses the game.
- An API restart wipes every game.
- More than one API instance can't share games.
- Nobody but the dealer can see the game.

`/Get` also reads, picks and writes back with nothing stopping two requests at once, so simultaneous draws can duplicate or lose a ball.

What you asked for:
1. The GameKey goes in the dealer's URL, so a refresh keeps the game.
2. State is stored in PostgreSQL through EF Core.
3. Live viewing: viewers scan a QR code on the dealer's screen, and new balls are pushed to them with SSE. Resetting the game must not break the live view.
4. Auto pilot draws every 10s until the dealer presses Stop. The dealer and viewers both see a countdown.

Decisions you made:
- Auto pilot runs **in the dealer's browser**.
- **Viewer pages are open** on ngrok, without basic auth. Everything else keeps basic auth.
- The QR holds a public **join link**. **Each person who scans it gets their own viewer ID.**
- The dealer sees a **live count of who's watching**.

## Design

### Identity and URLs
- **GameKey** (an existing GUID) stays the dealer's secret.
  - Dealer URL: `/?game=<GameKey>`, set with `history.replaceState` on the first draw.
  - On load, if `?game=` is present, the page fetches state, which does not draw a ball, and reconnects SSE.
- **JoinCode** is a public GUID, one per game, created along with the game row. The QR encodes `<origin>/watch.html?join=<JoinCode>`.
- **ViewerId**: when `watch.html` opens with `?join=`, it calls `POST /viewers?join=` and receives a new ViewerId. It then switches its URL to `watch.html?v=<ViewerId>`, so a refresh keeps the same viewer. A viewer never sees the GameKey.
- **Reset** keeps the same GameKey and JoinCode. It deletes the draws, adds 1 to `Round` and stops auto pilot. Viewer streams stay open and receive the cleared board, so the live view doesn't break.

### Data (EF Core 8, Npgsql, migrations)
- `Game`: `Id` (= GameKey), `JoinCode` (unique), `Round`, `AutoPilotNextDrawAt` (nullable timestamptz), `CreatedAt`, `UpdatedAt`.
- `Draw`: `GameId`, `Seq`, `Ball`.
  - Primary key `(GameId, Seq)`, unique `(GameId, Ball)`.
  - This replaces the old `Dictionary<int,string>`.
- `Viewer`: `Id`, `GameId`, `CreatedAt`, `LastSeenAt`.
- A **draw** is one transaction: `SELECT … FOR UPDATE` on the game row, then pick a ball and insert it. That serialises draws for a game even across several API instances, and the unique index is a backstop.
- A game row is created lazily, on the first `/Get` or `/State`.
- `/GetGameKey` is unchanged: it still returns a fresh GUID and writes nothing.

### API
- **Existing endpoints:** `GET /GetGameKey`, `GET /Get?GameKey=` (draw) and `POST /Reset?GameKey=` keep their shapes. Postgres now stores the data.
- **New, dealer only (by GameKey):**
  - `GET /State?GameKey=` returns a snapshot.
  - `GET /Events?GameKey=` is the dealer's SSE stream.
  - `POST /AutoPilot?GameKey=&on=true|false`:
    - On sets `AutoPilotNextDrawAt = now + interval`. Off clears it.
    - While it's on, each draw moves the next draw time forward.
    - It turns off automatically at 75 balls and on reset.
    - The interval comes from config `AutoPilot:IntervalSeconds`, default 10. Tests lower it.
- **New, public:**
  - `POST /viewers?join=` returns `{viewerId}`.
  - `GET /watch/{viewerId}/state` and `GET /watch/{viewerId}/events` are the viewer's snapshot and SSE stream.
  - An unknown ID returns 404.
  - None of these can draw or reset.
- **Snapshot**, used for both the state calls and every SSE event: `{round, numberOfDrew, currentNumber, history, autoPilot:{on, nextDrawAt}, serverTime, viewerCount}`. Dealer snapshots also include `joinCode`.
  - Every SSE event is a full snapshot (`event: state`), sent once when the client connects and again after every change.
  - A client that reconnects therefore catches up by itself, and reset needs no special case.
  - The server sends a `: ping` heartbeat every 15s and the header `X-Accel-Buffering: no`.

### Fan-out across instances
- A change runs Postgres `NOTIFY game_events, '<gameId>'` inside its transaction.
- On each instance, one background service holds a dedicated Npgsql connection that `LISTEN`s and pushes the game ID to an in-process `GameEventHub`, which keeps one channel per SSE subscriber.
- Each subscriber then reloads its snapshot from the database and sends it.
- Any number of API instances get correct live updates with no Redis.

### Viewer count
- While a viewer's SSE stream is open, its handler updates `LastSeenAt` on every heartbeat.
- The count is the number of viewers with `LastSeenAt` in the last 30s.
- When a stream connects or disconnects, the server sends a NOTIFY so the dealer's count updates. This is correct across instances.

### Auto pilot (in the dealer's browser) and countdown
- **Dealer flow:** pressing ▶ Auto pilot calls `POST /AutoPilot?on=true`. The dealer page then runs a `setTimeout` until the snapshot's `nextDrawAt` and calls `/Get`, and the server sets the next time. Pressing ■ Stop calls `on=false`.
  - A manual draw while auto pilot is on restarts the countdown.
  - If the dealer refreshes, the page sees auto pilot is on and resumes it.
- **Countdown** for both dealer and viewers: `clockOffset = serverTime − Date.now()` when a snapshot arrives, and `remaining = nextDrawAt − (Date.now() + clockOffset)`. The display reads "Next ball in 7s".
- If the countdown is more than 5s overdue (the dealer's tab closed or went to sleep), viewers see "Waiting for dealer…".

### UI
**Dealer page (`index.html`):**
```
[BINGO App]        [▶ Auto pilot] [🔊 Sound] [↺ Reset]
        ( B12 )   Next ball in 7s        ┌─────────┐
  History …                              │ ▓▓ QR ▓▓ │ Scan to watch
  Called by letter board …               └─────────┘ 👁 3 watching
```
- If the page was opened on `localhost`, a hint under the QR says to open it through the LAN or ngrok address, so that phones can reach the link.

**Viewer page (`watch.html`, new):**
- The same ball, history and board, with no controls.
- It shows the countdown and a "Live", "Reconnecting…" or "Waiting for dealer…" indicator.

**New JS files:**
- `live.js`: pure functions for URL helpers (game, join and viewer parameters, building the join URL), applying a snapshot, the countdown and overdue check, and the connection status.
- `autopilot.js`: the scheduler, with the clock and timers injected so tests can control them.
- `vendor/qrcode.js`: the MIT `qrcode-generator` library, copied in with its license header. No CDN, so it works offline on a LAN.

### Infra
- **`compose.yaml`:** add `db: postgres:17-alpine` with a `pgdata` volume and a healthcheck.
  - The `api` service waits for the database to be healthy and gets `ConnectionStrings__Bingo`.
  - `.env.example` gains `POSTGRES_PASSWORD`.
- **Migrations** are applied when the API starts. That's fine for one API container. With several replicas, run them as a separate step; this will be noted in the README and compose comments.
- **`nginx.conf` `/api/`:** `proxy_http_version 1.1`, `proxy_set_header Connection ''`, `proxy_buffering off`, `proxy_read_timeout 1h`.
- **`ngrok/render-policy.sh`:** the basic-auth action gets a filter expression that exempts `/watch.html`, the static JS it needs, `/api/viewers` and `/api/watch/*`. The exact syntax of ngrok's filter expressions gets checked against their docs during implementation.
- **`UI/Dockerfile`:** copy `watch.html`, `live.js`, `autopilot.js` and `vendor/`.

## Implementation order (TDD: red, then green, then refactor for each step)
1. Commit this design to `docs/superpowers/specs/2026-11-27-live-viewing-postgres-design.md`, then run `superpowers:writing-plans` to produce the step-by-step plan.
2. **Database groundwork:**
   - Packages `Npgsql.EntityFrameworkCore.PostgreSQL` 8.x and `Microsoft.EntityFrameworkCore.Design`.
   - `BingoDbContext`, the entities and the first migration.
   - In the test project: `Testcontainers.PostgreSql`, plus a `[ModuleInitializer]` that starts one container and sets `ConnectionStrings__Bingo`, so the existing `CorsIntegrationTests` still pass without being edited.
3. **Move the ball choice** in `BingoController.Index` into a pure `BallPicker` (`Next(IReadOnlyCollection<string> called, Random)`), with unit tests.
4. **`GameStore`** (`DrawAsync`, `ResetAsync`, `GetSnapshotAsync`). Switch the controller off `IMemoryCache`, and `Program.cs` stops calling `AddMemoryCache`.
5. **Live updates:** `GameEventHub`, the LISTEN/NOTIFY listener, `/Events`, `/State`, the SSE writer and the nginx changes.
6. **Viewers:** join, watch state and events, and the heartbeat-based viewer count.
7. **Auto pilot:** the endpoint, `nextDrawAt` handling, the interval config, and switching off on game over and on reset.
8. **UI:** `live.js` and `autopilot.js` with unit tests. Then the dealer page: URL parameter and refresh, SSE, QR, viewer count, auto pilot button and countdown.
9. **`watch.html`.**
10. **ngrok policy** exemption, plus the Dockerfile and compose changes.
11. **Coverage and mutation tooling:**
    - JS: add the new files to the `test:coverage` include list and to Stryker's `mutate` list.
    - .NET: `coverlet.msbuild` with a 90% threshold, and Stryker.NET (`dotnet-stryker` in a tool manifest) on `BallPicker`, the snapshot code and the store logic, with a break threshold of 80.

## Tests
- **C# unit:**
  - `BallPicker`: letters stay balanced and no ball repeats over a full 75-ball game.
  - Building snapshots.
  - Auto pilot timing rules.
  - `GameEventHub` subscribe, unsubscribe and fan-out.
- **C# integration** (WebApplicationFactory with Testcontainers Postgres):
  - A game survives a new API instance on the same database.
  - 20 simultaneous `/Get` calls give 20 different balls.
  - Reset keeps the JoinCode and an open stream receives the cleared snapshot.
  - Each `POST /viewers` returns a unique ID, and a ViewerId is rejected by the dealer endpoints.
  - The viewer count goes up and down.
  - Turning auto pilot on and drawing moves `nextDrawAt` forward, and it turns off at 75 balls and on reset.
  - **Two factories on one database**: a draw on instance A reaches an SSE subscriber on instance B, which proves the LISTEN/NOTIFY fan-out.
- **JS unit** (`node --test`, c8 at 90% or more, Stryker): `live.js` and `autopilot.js`, with fake clock and timers.
- **Playwright e2e** (mocked API):
  - The dealer URL gets `?game=` after the first draw, and a refresh restores the board through `/State`.
  - The QR shows the join URL.
  - The auto pilot toggle and countdown, driven by `page.clock`.
  - The viewer page renders a mocked `text/event-stream`.
  - The viewer page shows "Waiting for dealer…" when the countdown is overdue.
- **Playwright integration** (real API and Postgres; `playwright.config.js` starts a Postgres container and passes the connection string):
  - Two browser contexts: the dealer draws and the viewer board updates live.
  - The dealer resets and the viewer board clears without a reload.
  - Auto pilot with `AutoPilot__IntervalSeconds=2`: the viewer sees the countdown and new balls.
  - Refreshing the dealer page mid-game keeps the board.
  - The viewer count goes up when a viewer joins.
- **Docker stack:**
  - A game survives `docker compose restart api`.
  - The watch page and SSE work through nginx.
  - Through ngrok, viewer paths return 200 without auth and the dealer page still returns 401.

## Existing tests that must change (you need to approve these)
Per your rules, I won't change any of these unless you approve this plan:
- `UI/tests/unit/ngrok-policy.test.js`, "renders a basic-auth traffic policy…": it checks the exact JSON, and the policy will now include the viewer-path exemption. The fix is to update the expected JSON.
- `UI/tests/e2e/api-host.spec.js`, "draws call the API on the host…": it expects the exact call list `['…/GetGameKey', '…/Get']`, and the dealer page will now also open `/Events`. The fix is to compare only the draw calls, keeping what the test checks.
- `UI/tests/integration/*.spec.js`: the test code doesn't change. They need Postgres running, which comes from the Playwright config.

## Verification
- `dotnet test` with coverage at 90% or more, and `dotnet stryker` at 80% or more.
- In `src/UI`: `npm run test:coverage`, `npm run test:mutation`, `npm run test:e2e` and `npm run test:integration`.
- `docker compose up --build`, then `npm run test:docker`.
- Manual check: open the dealer page on the LAN IP, scan the QR with a phone, then draw, reset and run auto pilot. The phone should follow along and the dealer should see "👁 1 watching".
