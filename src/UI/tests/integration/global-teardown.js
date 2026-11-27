const { execSync } = require('child_process');

// Removes the throwaway Postgres started by scripts/integration-api.sh.
module.exports = async () => {
  if (!process.env.INTEGRATION) return;
  try { execSync('docker rm -f bingo-it-db', { stdio: 'ignore' }); } catch { /* ignore */ }
};
