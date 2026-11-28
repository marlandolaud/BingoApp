namespace BingoBongo.Games;

public static class BallPicker
{
    private static readonly string[] Letters = ["B", "I", "N", "G", "O"];

    /// <summary>
    /// Balanced draw: pick among the letters called least so far (that still
    /// have numbers left), then a random uncalled number within that letter.
    /// Keeps every letter within one call of the others. Returns null when all 75 are called.
    /// </summary>
    public static string? Next(IReadOnlyCollection<string> called, Random rand)
    {
        var calledSet = called.ToHashSet();
        var remainingByLetter = Letters
            .Select((letter, i) => (
                Letter: letter,
                Remaining: Enumerable.Range(i * 15 + 1, 15)
                    .Select(n => $"{letter}{n}")
                    .Where(ball => !calledSet.Contains(ball))
                    .ToList()))
            .Where(x => x.Remaining.Count > 0)
            .ToList();
        if (remainingByLetter.Count == 0)
        {
            return null;
        }

        var mostRemaining = remainingByLetter.Max(x => x.Remaining.Count);
        var candidates = remainingByLetter.Where(x => x.Remaining.Count == mostRemaining).ToList();
        var chosen = candidates[rand.Next(candidates.Count)];
        return chosen.Remaining[rand.Next(chosen.Remaining.Count)];
    }
}
