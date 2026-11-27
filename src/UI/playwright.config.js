const { defineConfig } = require('@playwright/test');

module.exports = defineConfig({
  testDir: './tests',
  globalTeardown: require.resolve('./tests/integration/global-teardown.js'),
  use: { baseURL: 'http://localhost:8080', headless: true },
  projects: [
    // UI tests with the API mocked via route interception
    { name: 'e2e', testMatch: /e2e\/.*\.spec\.js/ },
    // full stack: real ASP.NET API on :5001 + static UI on :8080
    { name: 'integration', testMatch: /integration\/.*\.spec\.js/ },
    // containerised stack: `docker compose up` (nginx UI + API + ngrok)
    { name: 'docker', testMatch: /docker\/.*\.spec\.js/ },
  ],
  webServer: process.env.DOCKER ? [] : [
    { command: 'npx http-server -p 8080 -c-1 -s .', url: 'http://localhost:8080', reuseExistingServer: true },
    ...(process.env.INTEGRATION
      ? [{
          command: 'sh scripts/integration-api.sh',
          url: 'http://localhost:5001/GetGameKey',
          reuseExistingServer: true,
          timeout: 120000,
        }]
      : []),
  ],
});
