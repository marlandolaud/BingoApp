using System.Net;
using System.Text.Json;
using BingoBongo.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace BingoBongo.Tests;

public class PersistenceTests
{
    [Fact]
    public async Task Startup_applies_migrations()
    {
        using var f = new BingoApiFactory();
        using var scope = f.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BingoDbContext>();

        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(0, await db.Games.CountAsync(g => g.Id == Guid.NewGuid()));
    }

    [Fact]
    public async Task Game_survives_a_new_api_instance()
    {
        var key = Guid.NewGuid();
        var drawn = new List<string>();
        using (var a = new BingoApiFactory())
        {
            var client = a.CreateClient();
            for (var i = 0; i < 3; i++)
            {
                var draw = await GetJson(client, $"/Get?GameKey={key}");
                drawn.Add(draw.GetProperty("currentNumber").GetString()!);
            }
        }

        using var b = new BingoApiFactory();
        var state = await GetJson(b.CreateClient(), $"/State?GameKey={key}");

        Assert.Equal(3, state.GetProperty("numberOfDrew").GetInt32());
        Assert.Equal(drawn, HistoryInOrder(state));
    }

    [Fact]
    public async Task Concurrent_draws_never_duplicate()
    {
        var key = Guid.NewGuid();
        using var f = new BingoApiFactory();
        var client = f.CreateClient();

        var responses = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(_ => client.GetAsync($"/Get?GameKey={key}")));

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        var balls = new List<string>();
        foreach (var r in responses)
        {
            var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
            balls.Add(json.GetProperty("currentNumber").GetString()!);
        }
        Assert.Equal(20, balls.Distinct().Count());

        var state = await GetJson(client, $"/State?GameKey={key}");
        Assert.Equal(20, state.GetProperty("numberOfDrew").GetInt32());
    }

    [Fact]
    public async Task Reset_clears_draws_keeps_join_code_and_bumps_round()
    {
        var key = Guid.NewGuid();
        using var f = new BingoApiFactory();
        var client = f.CreateClient();
        await GetJson(client, $"/Get?GameKey={key}");
        await GetJson(client, $"/Get?GameKey={key}");
        var before = await GetJson(client, $"/State?GameKey={key}");

        var reset = await client.PostAsync($"/Reset?GameKey={key}", null);
        Assert.Equal(HttpStatusCode.OK, reset.StatusCode);

        var after = await GetJson(client, $"/State?GameKey={key}");
        Assert.Equal(2, before.GetProperty("numberOfDrew").GetInt32());
        Assert.Equal(0, after.GetProperty("numberOfDrew").GetInt32());
        Assert.Empty(HistoryInOrder(after));
        Assert.Equal(before.GetProperty("joinCode").GetGuid(), after.GetProperty("joinCode").GetGuid());
        Assert.Equal(before.GetProperty("round").GetInt32() + 1, after.GetProperty("round").GetInt32());
    }

    [Fact]
    public async Task Seventy_sixth_draw_is_game_over()
    {
        var key = Guid.NewGuid();
        using var f = new BingoApiFactory();
        var client = f.CreateClient();
        for (var i = 0; i < 75; i++)
        {
            await GetJson(client, $"/Get?GameKey={key}");
        }

        var last = await client.GetAsync($"/Get?GameKey={key}");

        Assert.Equal(HttpStatusCode.OK, last.StatusCode);
        Assert.Equal("Game Over!", await last.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_response_shape_is_unchanged()
    {
        using var f = new BingoApiFactory();
        var json = await GetJson(f.CreateClient(), $"/Get?GameKey={Guid.NewGuid()}");

        foreach (var name in new[] { "numberOfDrew", "currentNumber", "history", "orderedDrawHistory", "orderedBingo" })
        {
            Assert.True(json.TryGetProperty(name, out _), $"missing {name}");
        }
        Assert.Equal(1, json.GetProperty("numberOfDrew").GetInt32());
        Assert.Equal(json.GetProperty("currentNumber").GetString(), json.GetProperty("history").GetProperty("0").GetString());
    }

    [Theory]
    [InlineData("GET", "/State")]
    [InlineData("GET", "/Get")]
    [InlineData("POST", "/Reset")]
    public async Task Endpoints_reject_an_empty_game_key(string method, string path)
    {
        using var f = new BingoApiFactory();
        var r = await f.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), $"{path}?GameKey={Guid.Empty}"));

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Equal("GameKey is empty", await r.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task State_creates_a_missing_game_with_round_one_and_auto_pilot_off()
    {
        using var f = new BingoApiFactory();
        var state = await GetJson(f.CreateClient(), $"/State?GameKey={Guid.NewGuid()}");

        Assert.Equal(1, state.GetProperty("round").GetInt32());
        Assert.Equal(0, state.GetProperty("numberOfDrew").GetInt32());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("currentNumber").ValueKind);
        Assert.False(state.GetProperty("autoPilot").GetProperty("on").GetBoolean());
        Assert.Equal(0, state.GetProperty("viewerCount").GetInt32());
        Assert.NotEqual(Guid.Empty, state.GetProperty("joinCode").GetGuid());
    }

    [Theory]
    [InlineData("GET", "/Get")]
    [InlineData("POST", "/Reset")]
    public async Task Writes_notify_game_events_with_the_game_key(string method, string path)
    {
        var key = Guid.NewGuid();
        await using var listener = new NpgsqlConnection(PostgresFixture.ConnectionString);
        await listener.OpenAsync();
        var payloads = new List<string>();
        listener.Notification += (_, e) => { lock (payloads) payloads.Add(e.Payload); };
        await using (var listen = new NpgsqlCommand("LISTEN game_events", listener))
        {
            await listen.ExecuteNonQueryAsync();
        }
        using var f = new BingoApiFactory();

        var r = await f.CreateClient().SendAsync(new HttpRequestMessage(new HttpMethod(method), $"{path}?GameKey={key}"));
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (true)
        {
            lock (payloads)
            {
                if (payloads.Contains(key.ToString())) break;
            }
            await listener.WaitAsync(timeout.Token);
        }
    }

    private static async Task<JsonElement> GetJson(HttpClient client, string url)
    {
        var r = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
    }

    private static List<string> HistoryInOrder(JsonElement state) =>
        state.GetProperty("history").EnumerateObject()
            .OrderBy(p => int.Parse(p.Name))
            .Select(p => p.Value.GetString()!)
            .ToList();
}
