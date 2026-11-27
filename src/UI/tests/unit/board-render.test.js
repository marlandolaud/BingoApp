const test = require('node:test');
const assert = require('node:assert/strict');
const { renderBoard, renderTrack } = require('../../board.js');

// minimal fake DOM: just what the render helpers touch
function fakeDocument() {
  const make = (tag) => ({
    tag, className: '', textContent: '', dataset: {}, attrs: {}, children: [], scope: undefined,
    appendChild(c) { this.children.push(c); return c; },
    setAttribute(k, v) { this.attrs[k] = v; },
    replaceChildren() { this.children = []; },
  });
  return { createElement: make };
}
function fakeTbody() {
  const doc = fakeDocument();
  const el = doc.createElement('tbody');
  el.ownerDocument = doc;
  return el;
}

test('renderBoard builds a row per letter with a th and 15 cells', () => {
  const tbody = fakeTbody();
  renderBoard(tbody, []);
  assert.equal(tbody.children.length, 5);
  const row = tbody.children[1];
  assert.equal(row.tag, 'tr');
  assert.equal(row.dataset.letter, 'I');
  assert.equal(row.children.length, 16);
  const th = row.children[0];
  assert.equal(th.tag, 'th');
  assert.equal(th.scope, 'row');
  assert.equal(th.children[0].className, 'letter');
  assert.equal(th.children[0].textContent, 'I');
  assert.equal(th.children[1].className, 'count');
  assert.equal(th.children[1].textContent, 0);
  assert.equal(row.children[1].tag, 'td');
  assert.equal(row.children[1].textContent, 16);
  assert.equal(row.children[1].className, 'cell');
  assert.equal(row.children[1].attrs['aria-label'], 'I16');
});

test('renderBoard marks called and last cells and counts', () => {
  const tbody = fakeTbody();
  renderBoard(tbody, ['B1', 'B2']);
  const b = tbody.children[0];
  assert.equal(b.children[0].children[1].textContent, 2);
  assert.equal(b.children[1].className, 'cell called');
  assert.equal(b.children[1].attrs['aria-label'], 'B1 called');
  assert.equal(b.children[2].className, 'cell called last');
  assert.equal(b.children[3].className, 'cell');
});

test('renderBoard replaces previous content', () => {
  const tbody = fakeTbody();
  renderBoard(tbody, []);
  renderBoard(tbody, ['B1']);
  assert.equal(tbody.children.length, 5);
});

function trackFixture(scrollWidth, clientWidth) {
  const track = fakeTbody();
  const scrolls = [];
  const container = { scrollWidth, clientWidth, scrollTo: (o) => scrolls.push(o) };
  return { track, container, scrolls };
}

test('renderTrack renders one small dot per draw and replaces old dots', () => {
  const { track, container } = trackFixture(10, 10);
  renderTrack(track, container, ['B5']);
  renderTrack(track, container, ['B5', 'O70']);
  assert.equal(track.children.length, 2);
  assert.equal(track.children[1].className, 'dot small');
  assert.equal(track.children[1].textContent, 'O70');
});

test('renderTrack scrolls to the end when the row overflows', () => {
  const { track, container, scrolls } = trackFixture(500, 100);
  renderTrack(track, container, ['B5']);
  assert.deepEqual(scrolls, [{ left: 500, behavior: 'smooth' }]);
});

test('renderTrack does not scroll when everything fits', () => {
  const { track, container, scrolls } = trackFixture(100, 100);
  renderTrack(track, container, ['B5']);
  assert.deepEqual(scrolls, []);
});
