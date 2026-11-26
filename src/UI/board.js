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

  const api = { buildBoard };
  if (typeof module !== 'undefined' && module.exports) module.exports = api;
  else root.BingoBoard = api;
})(this);
