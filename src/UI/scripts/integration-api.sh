#!/bin/sh
# Starts a throwaway Postgres (bingo-it-db on :55433) and then the real API on :5001.
# Done here because Playwright's webServer does not wait for globalSetup.
if docker inspect bingo-it-db >/dev/null 2>&1; then
  docker start bingo-it-db >/dev/null
else
  docker run -d --name bingo-it-db -p 55433:5432 -e POSTGRES_PASSWORD=bingo -e POSTGRES_DB=bingo postgres:17-alpine >/dev/null
fi
until docker exec bingo-it-db pg_isready -U postgres >/dev/null 2>&1; do sleep 1; done
exec env ConnectionStrings__Bingo='Host=localhost;Port=55433;Database=bingo;Username=postgres;Password=bingo' \
  AutoPilot__IntervalSeconds=2 Live__HeartbeatSeconds=2 DOTNET_ROLL_FORWARD=Major \
  dotnet run --project ../BingoBongo/BingoBongo.csproj --no-launch-profile --urls http://localhost:5001
