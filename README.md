# BingoApp
## Run with Docker Compose

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
Set `NGROK_URL` in `.env` to use a reserved/static domain. ngrok refuses to start if the basic-auth
credentials are missing or the password is shorter than 8 characters, so the tunnel is never exposed unprotected.

Tests against the running stack: `cd src/UI && npm run test:docker` (set `UI_PORT` if you changed it).
The ngrok tests run once the tunnel is up, using `BASIC_AUTH_USER` / `BASIC_AUTH_PASS` from the environment.
