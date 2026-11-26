const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs');
const path = require('node:path');
const vm = require('node:vm');
const { apiBase } = require('../../api.js');

test('uses the host the page was loaded from', () => {
  assert.equal(apiBase({ protocol: 'http:', hostname: '192.168.1.118' }), 'http://192.168.1.118:5001');
});

test('keeps localhost when loaded locally', () => {
  assert.equal(apiBase({ protocol: 'http:', hostname: 'localhost' }), 'http://localhost:5001');
});

test('keeps the page protocol', () => {
  assert.equal(apiBase({ protocol: 'https:', hostname: 'bingo.lan' }), 'https://bingo.lan:5001');
});

test('wraps IPv6 hosts in brackets', () => {
  assert.equal(apiBase({ protocol: 'http:', hostname: 'fe80::1' }), 'http://[fe80::1]:5001');
});

test('does not double-bracket an already bracketed IPv6 host', () => {
  assert.equal(apiBase({ protocol: 'http:', hostname: '[::1]' }), 'http://[::1]:5001');
});

test('falls back to localhost when opened from a file', () => {
  assert.equal(apiBase({ protocol: 'file:', hostname: '' }), 'http://localhost:5001');
});

test('falls back to localhost for a file opened from a network share', () => {
  assert.equal(apiBase({ protocol: 'file:', hostname: 'nas' }), 'http://localhost:5001');
});

test('falls back to localhost when there is no hostname', () => {
  assert.equal(apiBase({ protocol: 'http:', hostname: '' }), 'http://localhost:5001');
});

test('exposes window.BingoApi when loaded as a browser script', () => {
  const file = path.join(__dirname, '../../api.js');
  const window = { process, __stryker__: globalThis.__stryker__ };
  vm.runInNewContext(fs.readFileSync(file, 'utf8'), window, { filename: file });
  assert.equal(window.BingoApi.apiBase({ protocol: 'http:', hostname: 'x' }), 'http://x:5001');
});
