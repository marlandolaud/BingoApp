const test = require('node:test');
const assert = require('node:assert/strict');
const { buildBoard } = require('../../board.js');

test('board has a row for each letter in BINGO order', () => {
  const board = buildBoard([]);
  assert.deepEqual(board.map(r => r.letter), ['B', 'I', 'N', 'G', 'O']);
});

test('each row holds its 15 numbers in ascending order', () => {
  const board = buildBoard([]);
  board.forEach((row, i) => {
    const expected = Array.from({ length: 15 }, (_, k) => i * 15 + k + 1);
    assert.deepEqual(row.numbers.map(c => c.number), expected);
  });
});

test('nothing is marked called on an empty history', () => {
  const board = buildBoard([]);
  assert.ok(board.every(r => r.numbers.every(c => c.called === false)));
  assert.ok(board.every(r => r.calledCount === 0));
});

test('called numbers are marked in their letter row only', () => {
  const board = buildBoard(['B7', 'O75', 'N31']);
  const called = board.flatMap(r => r.numbers.filter(c => c.called).map(c => r.letter + c.number));
  assert.deepEqual(called, ['B7', 'N31', 'O75']);
});

test('calledCount counts calls per letter', () => {
  const board = buildBoard(['B1', 'B15', 'G50']);
  assert.deepEqual(board.map(r => r.calledCount), [2, 0, 0, 1, 0]);
});

test('accepts the API history object keyed by draw order', () => {
  const board = buildBoard({ 0: 'I16', 1: 'I30' });
  assert.equal(board[1].calledCount, 2);
  assert.equal(board[1].numbers[0].called, true);
  assert.equal(board[1].numbers[14].called, true);
});

test('treats null or undefined history as empty', () => {
  assert.equal(buildBoard(null).reduce((s, r) => s + r.calledCount, 0), 0);
  assert.equal(buildBoard(undefined).reduce((s, r) => s + r.calledCount, 0), 0);
});

test('the most recent call is flagged as last', () => {
  const board = buildBoard({ 0: 'B3', 1: 'G55' });
  const last = board.flatMap(r => r.numbers.filter(c => c.last).map(c => r.letter + c.number));
  assert.deepEqual(last, ['G55']);
});

test('no cell is flagged last on an empty history', () => {
  assert.ok(buildBoard([]).every(r => r.numbers.every(c => c.last === false)));
});

test('exposes window.BingoBoard when loaded as a browser script', () => {
  const fs = require('node:fs');
  const path = require('node:path');
  const vm = require('node:vm');
  const src = fs.readFileSync(path.join(__dirname, '../../board.js'), 'utf8');
  // share Stryker's mutant-switching state (if present) so mutation testing reaches this path
  const window = { process, __stryker__: globalThis.__stryker__ };
  vm.runInNewContext(src, window, { filename: path.join(__dirname, '../../board.js') });
  assert.equal(typeof window.BingoBoard.buildBoard, 'function');
  assert.equal(window.BingoBoard.buildBoard(['B1'])[0].calledCount, 1);
});
