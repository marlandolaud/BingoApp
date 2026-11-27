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

const EXPR = "!(req.url.path in ['/watch.html','/board.js','/api.js','/config.js','/live.js','/api/viewers'] || req.url.path.matches('^/api/watch/[0-9a-fA-F-]{36}/(state|events)$'))";

function expression() {
  const { out } = render({ BASIC_AUTH_USER: 'host', BASIC_AUTH_PASS: 'letmein123' });
  return JSON.parse(out).on_http_request[0].expressions[0];
}

test('renders a basic-auth traffic policy from the env credentials', () => {
  const { code, out } = render({ BASIC_AUTH_USER: 'host', BASIC_AUTH_PASS: 'letmein123' });
  assert.equal(code, 0);
  assert.deepEqual(JSON.parse(out), {
    on_http_request: [{ expressions: [EXPR], actions: [{ type: 'basic-auth', config: { credentials: ['host:letmein123'] } }] }],
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

function parseExemption() {
  const expr = expression();
  const list = expr.match(/req\.url\.path in \[([^\]]*)\]/)[1].split(',').map(x => x.trim().replace(/^'|'$/g, ''));
  const patterns = [...expr.matchAll(/matches\('([^']*)'\)/g)].map(m => m[1]);
  return { expr, list, patterns };
}
const isExempt = ({ list, patterns }, p) => list.includes(p) || patterns.some(r => new RegExp(r).test(p));

test('exempts exactly the viewer paths', () => {
  const { expr, list, patterns } = parseExemption();
  assert.deepEqual(list, ['/watch.html', '/board.js', '/api.js', '/config.js', '/live.js', '/api/viewers']);
  assert.deepEqual(patterns, ['^/api/watch/[0-9a-fA-F-]{36}/(state|events)$']);
  assert.ok(!expr.includes('startsWith'), 'no prefix match (path traversal)');
});

test('does not exempt dealer paths', () => {
  const ex = parseExemption();
  for (const p of ['/', '/index.html', '/autopilot.js', '/api/GetGameKey', '/api/Get', '/api/Reset', '/api/State', '/api/Events', '/api/AutoPilot']) {
    assert.ok(!isExempt(ex, p), `${p} must not be exempt`);
  }
});

test('exempts viewer state and events paths for a guid', () => {
  const ex = parseExemption();
  const g = '123e4567-e89b-12d3-a456-426614174000';
  assert.ok(isExempt(ex, `/api/watch/${g}/state`));
  assert.ok(isExempt(ex, `/api/watch/${g}/events`));
});

test('does not exempt traversal or malformed paths under /api/watch/', () => {
  const ex = parseExemption();
  const g = '123e4567-e89b-12d3-a456-426614174000';
  for (const p of ['/api/watch/../GetGameKey', '/api/watch/%2e%2e/Get', '/api/watch//GetGameKey',
    `/api/watch/${g}/state/../../Reset`, `/api/watch/${g}/STATE`, '/api/watch/', `/api/watch/${g}/state\n/x`]) {
    assert.ok(!isExempt(ex, p), `${JSON.stringify(p)} must not be exempt`);
  }
});
