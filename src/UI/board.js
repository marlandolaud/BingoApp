// Builds the called-numbers board: one row per BINGO letter, 15 numbers each.
// Works in the browser (window.BingoBoard) and in Node (module.exports) for tests.
(function (root) {
  const LETTERS = ['B', 'I', 'N', 'G', 'O'];

  function buildBoard(history) {
    const draws = Object.values(history || {});
    const called = new Set(draws);
    const last = draws[draws.length - 1];

    return LETTERS.map((letter, i) => {
      const numbers = Array.from({ length: 15 }, (_, k) => {
        const number = i * 15 + k + 1;
        const ball = letter + number;
        return { number, called: called.has(ball), last: ball === last };
      });
      return { letter, numbers, calledCount: numbers.filter(c => c.called).length };
    });
  }

  // Fills <tbody> with one row per letter (markup shared by the dealer and viewer pages).
  function renderBoard(tbody, history) {
    const doc = tbody.ownerDocument;
    tbody.replaceChildren();
    for (const row of buildBoard(history)) {
      const tr = doc.createElement('tr');
      tr.dataset.letter = row.letter;
      const th = doc.createElement('th');
      th.scope = 'row';
      const letter = doc.createElement('span');
      letter.className = 'letter';
      letter.textContent = row.letter;
      th.appendChild(letter);
      const count = doc.createElement('span');
      count.className = 'count';
      count.textContent = row.calledCount;
      th.appendChild(count);
      tr.appendChild(th);
      for (const c of row.numbers) {
        const td = doc.createElement('td');
        td.className = 'cell' + (c.called ? ' called' : '') + (c.last ? ' last' : '');
        td.textContent = c.number;
        td.setAttribute('aria-label', row.letter + c.number + (c.called ? ' called' : ''));
        tr.appendChild(td);
      }
      tbody.appendChild(tr);
    }
  }

  // Fills the history track with one dot per draw and scrolls the container to the newest.
  function renderTrack(track, container, history) {
    const doc = track.ownerDocument;
    track.replaceChildren();
    for (const value of Object.values(history || {})) {
      const d = doc.createElement('div');
      d.className = 'dot small';
      d.textContent = value;
      track.appendChild(d);
    }
    const raf = typeof requestAnimationFrame === 'function' ? requestAnimationFrame : (fn) => fn();
    raf(() => {
      if (container.scrollWidth > container.clientWidth) {
        container.scrollTo({ left: container.scrollWidth, behavior: 'smooth' });
      }
    });
  }

  const api = { buildBoard, renderBoard, renderTrack };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.BingoBoard = api;
})(this);
