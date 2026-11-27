using System.Text.RegularExpressions;
using BingoBongo.Games;

namespace BingoBongo.Tests;

[Trait("Category", "Unit")]
public class BallPickerTests
{
    private static readonly string[] Letters = ["B", "I", "N", "G", "O"];

    private static IEnumerable<string> AllBalls(string letter)
    {
        var i = Array.IndexOf(Letters, letter);
        return Enumerable.Range(i * 15 + 1, 15).Select(n => $"{letter}{n}");
    }

    [Fact]
    public void Draws_all_75_distinct_balls_then_null()
    {
        var rand = new Random(42);
        var called = new HashSet<string>();
        for (var i = 0; i < 75; i++)
        {
            var ball = BallPicker.Next(called, rand);
            Assert.NotNull(ball);
            Assert.True(called.Add(ball!), $"{ball} drawn twice");
        }

        Assert.Equal(75, called.Count);
        foreach (var ball in called)
        {
            var m = Regex.Match(ball, @"^([BINGO])(\d+)$");
            Assert.True(m.Success, ball);
            var i = Array.IndexOf(Letters, m.Groups[1].Value);
            var n = int.Parse(m.Groups[2].Value);
            Assert.InRange(n, i * 15 + 1, i * 15 + 15);
        }

        Assert.Null(BallPicker.Next(called, rand));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Letters_stay_within_one_of_each_other(int seed)
    {
        var rand = new Random(seed);
        var called = new HashSet<string>();
        for (var i = 0; i < 75; i++)
        {
            called.Add(BallPicker.Next(called, rand)!);
            var counts = Letters.Select(l => called.Count(b => b.StartsWith(l))).ToList();
            Assert.True(counts.Max() - counts.Min() <= 1, $"after {i + 1} draws: {string.Join(",", counts)}");
        }
    }

    [Fact]
    public void Picks_from_letter_with_most_remaining()
    {
        var called = new[] { "B", "I", "N", "G" }
            .SelectMany(AllBalls)
            .Where(b => b != "G60")
            .ToHashSet();

        for (var seed = 0; seed < 20; seed++)
        {
            var ball = BallPicker.Next(called, new Random(seed));
            Assert.StartsWith("O", ball);
        }
    }

    [Fact]
    public void Never_returns_a_called_ball()
    {
        var called = Letters.SelectMany(AllBalls).Where(b => b != "N42").ToHashSet();

        Assert.Equal("N42", BallPicker.Next(called, new Random(1)));
    }
}
