const { defineConfig } = require('@playwright/test');

module.exports = defineConfig({
  testDir: './tests',
  use: { baseURL: 'http://localhost:8080', headless: true },
  projects: [
    // UI tests with the API mocked via route interception
    { name: 'e2e', testMatch: /e2e\/.*\.spec\.js/ },
    // full stack: real ASP.NET API on :5001 + static UI on :8080
    { name: 'integration', testMatch: /integration\/.*\.spec\.js/ },
  ],
  webServer: [
    { command: 'npx http-server -p 8080 -c-1 -s .', url: 'http://localhost:8080', reuseExistingServer: true },
    ...(process.env.INTEGRATION
      ? [{
          command: 'dotnet run --project ../BingoBongo/BingoBongo.csproj --no-launch-profile --urls http://localhost:5001',
          url: 'http://localhost:5001/GetGameKey',
          reuseExistingServer: true,
          timeout: 120000,
        }]
      : []),
  ],
});
