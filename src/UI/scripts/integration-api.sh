#!/bin/sh
set -eu
# Starts a throwaway Postgres (bingo-it-db on :55433) and then the real API on :5001.
# Done here because Playwright's webServer does not wait for globalSetup.
if docker inspect bingo-it-db >/dev/null 2>&1; then
  docker start bingo-it-db >/dev/null
else
  docker run -d --name bingo-it-db -p 55433:5432 -e POSTGRES_PASSWORD=bingo -e POSTGRES_DB=bingo postgres:17-alpine >/dev/null
fi
ready=0
err=''
for _ in $(seq 1 60); do
  # probe over TCP with a real query: the first-boot init server is socket-only
  if err=$(docker exec -e PGPASSWORD=bingo bingo-it-db psql -h 127.0.0.1 -U postgres -d bingo -c 'select 1' 2>&1 >/dev/null); then ready=1; break; fi
  sleep 1
done
if [ "$ready" != 1 ]; then echo "bingo-it-db did not become ready within 60s: $err" >&2; exit 1; fi
exec env ConnectionStrings__Bingo='Host=localhost;Port=55433;Database=bingo;Username=postgres;Password=bingo' \
  AutoPilot__IntervalSeconds=2 Live__HeartbeatSeconds=2 \
  dotnet run --project ../BingoBongo/BingoBongo.csproj --no-launch-profile --urls http://localhost:5001
