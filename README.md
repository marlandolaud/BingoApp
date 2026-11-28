# BingoApp

A bingo caller (ASP.NET Core 10 API + static UI) with live read-only viewing for players.

## Run locally

```sh
cd src/UI
npm install
npm run db      # Postgres 17 in Docker on localhost:55432 (container bingo-dev-db)
npm run api     # ASP.NET API (migrations run on startup)
npm run serve   # static UI on http://localhost:8080
```

## How live viewing works

1. The caller opens the UI and draws the first ball (or starts auto pilot). That creates the game,
   and the invite card then shows a QR code carrying a join code. The card stays hidden until then.
2. A player scans the QR code, which opens the viewer page (`watch.html`) with the join code.
3. The page registers the player (`POST /api/viewers?join=...`) and gets a personal viewer link
   (`watch.html?v=<viewerId>`). Reloading or reopening that link resumes watching.
4. The viewer receives the board, drawn balls and history over server-sent events and cannot draw,
   reset or control auto pilot. The caller sees a live viewer count.

## Auto pilot

The caller's "Auto pilot" button draws balls on a timer so you can run a game hands-free. It can be
toggled off at any time; viewers see each draw as it happens. Each automatic draw names the tick it is
for, so two caller tabs open on the same game still draw one ball per tick. If a draw fails (for example
while the API restarts), the caller page retries it a couple of seconds later.

## Run with Docker Compose (includes Postgres)

```sh
cd src
cp .env.example .env      # fill in NGROK_AUTHTOKEN and BASIC_AUTH_USER / BASIC_AUTH_PASS
docker compose up -d --build
```

| Service | What it does |
|---------|--------------|
| `api`   | ASP.NET API (not published; only reachable through `ui`) |
| `ui`    | nginx serving the UI on `http://localhost:${UI_PORT:-8080}` and proxying `/api/*` to `api` |
| `ngrok` | Public HTTPS tunnel to `ui`, protected by HTTP basic auth (ngrok traffic policy) |

The public URL is shown in the ngrok inspector at http://localhost:4040 (or `docker compose logs ngrok`).
Set `NGROK_URL` in `.env` to use a reserved/static domain.
`POSTGRES_PASSWORD` (default `bingo`) is only applied when the `pgdata` volume is first created; changing
it later does not change the password of the existing database, so change it inside Postgres too
(`ALTER USER postgres PASSWORD '...'`) or the API can no longer connect. ngrok refuses to start if the basic-auth
credentials are missing or the password is shorter than 8 characters, so the tunnel is never exposed unprotected.

Tests against the running stack: `cd src/UI && npm run test:docker` (set `UI_PORT` if you changed it).
The ngrok tests run once the tunnel is up, using `BASIC_AUTH_USER` / `BASIC_AUTH_PASS` from the environment.

### Before you expose the tunnel

With the ngrok tunnel up, run `npm run test:docker` once (from `src/UI`). It confirms the viewer paths
open without basic auth and that `/api/watch/..%2fGetGameKey` returns 401.

### Migrations with several replicas

The API applies EF Core migrations on startup. If you run more than one API replica, run migrations as
a separate step (for example `dotnet ef database update` from `src/BingoBongo`) before starting the replicas.

## Tests

| What | Command |
|------|---------|
| .NET unit + integration (needs Docker for a Postgres Testcontainer) | `cd src && dotnet test` |
| .NET coverage gate (90% line and branch) | `cd src && dotnet test /p:CollectCoverage=true /p:Threshold=90 /p:ThresholdType=line%2cbranch /p:ExcludeByFile="**/Migrations/*.cs%2c**/WeatherForecast.cs"` |
| .NET mutation testing (unit tests only, score must stay at 80 or more) | `cd src && dotnet tool restore && cd BingoBongo.Tests && BINGO_SKIP_DB=1 dotnet stryker` |
| UI unit tests + 90% coverage | `cd src/UI && npm run test:coverage` |
| UI mutation testing | `npm run test:mutation` |
| Playwright e2e (mocked API) | `npm run test:e2e` |
| Playwright against the real API (starts on port 5001) | `npm run test:integration` |
| Playwright against the Docker stack | `docker compose up -d --build` then `npm run test:docker` |

`BINGO_SKIP_DB=1` stops the test run from starting the Postgres container, which unit-only mutation runs do not need.
