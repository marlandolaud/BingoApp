const test = require('node:test');
const assert = require('node:assert/strict');
const { spawnSync } = require('node:child_process');
const path = require('node:path');

const fs = require('node:fs');

// Walk up to src/ngrok so this also works from Stryker's sandbox copy of UI/.
function findScript(dir = __dirname) {
  const candidate = path.join(dir, 'ngrok', 'render-policy.sh');
  if (fs.existsSync(candidate)) return candidate;
  const parent = path.dirname(dir);
  if (parent === dir) throw new Error('ngrok/render-policy.sh not found');
  return findScript(parent);
}
const SCRIPT = findScript();

function render(env) {
  const r = spawnSync('sh', [SCRIPT], { env: { PATH: process.env.PATH, ...env }, encoding: 'utf8' });
  return { code: r.status, out: r.stdout, err: r.stderr };
}

test('renders a basic-auth traffic policy from the env credentials', () => {
  const { code, out } = render({ BASIC_AUTH_USER: 'host', BASIC_AUTH_PASS: 'letmein123' });
  assert.equal(code, 0);
  assert.deepEqual(JSON.parse(out), {
    on_http_request: [{ actions: [{ type: 'basic-auth', config: { credentials: ['host:letmein123'] } }] }],
  });
});

test('escapes quotes and backslashes in the password', () => {
  const { code, out } = render({ BASIC_AUTH_USER: 'host', BASIC_AUTH_PASS: 'a"b\\c-long-pw' });
  assert.equal(code, 0);
  assert.deepEqual(JSON.parse(out).on_http_request[0].actions[0].config.credentials, ['host:a"b\\c-long-pw']);
});

test('refuses to start without a username', () => {
  const { code, err } = render({ BASIC_AUTH_PASS: 'letmein123' });
  assert.notEqual(code, 0);
  assert.match(err, /BASIC_AUTH_USER/);
});

test('refuses to start without a password', () => {
  const { code, err } = render({ BASIC_AUTH_USER: 'host' });
  assert.notEqual(code, 0);
  assert.match(err, /BASIC_AUTH_PASS/);
});

test('refuses a username containing a colon', () => {
  const { code, err } = render({ BASIC_AUTH_USER: 'a:b', BASIC_AUTH_PASS: 'letmein123' });
  assert.notEqual(code, 0);
  assert.match(err, /colon/);
});

test('refuses a password shorter than 8 characters (ngrok minimum)', () => {
  const { code, err } = render({ BASIC_AUTH_USER: 'host', BASIC_AUTH_PASS: 'short' });
  assert.notEqual(code, 0);
  assert.match(err, /8/);
});

test('accepts a password of exactly 8 characters', () => {
  assert.equal(render({ BASIC_AUTH_USER: 'host', BASIC_AUTH_PASS: '12345678' }).code, 0);
});
