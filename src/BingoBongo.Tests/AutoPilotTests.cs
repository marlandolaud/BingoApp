using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace BingoBongo.Tests;

public class AutoPilotTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    private static (WebApplicationFactory<Program> Factory, FakeTimeProvider Clock) Create()
    {
        var clock = new FakeTimeProvider(DateTimeOffset.UnixEpoch.AddDays(20000));
        var factory = new BingoApiFactory().WithWebHostBuilder(b =>
            b.ConfigureServices(s => s.AddSingleton<TimeProvider>(clock)));
        return (factory, clock);
    }

    private static Task SetAutoPilot(HttpClient c, Guid key, bool on) =>
        c.PostAsync($"/AutoPilot?GameKey={key}&on={on.ToString().ToLowerInvariant()}", null);

    [Fact]
    public async Task AutoPilot_on_sets_next_draw_at_and_pushes_snapshot()
    {
        var key = Guid.NewGuid();
        var (f, clock) = Create();
        using var _ = f;
        var client = f.CreateClient();
        using var cts = new CancellationTokenSource(Timeout);
        await using var frames = SseClient.ReadStatesAsync(client, $"/Events?GameKey={key}", cts.Token).GetAsyncEnumerator(cts.Token);
        await frames.MoveNextAsync();

        var response = await client.PostAsync($"/AutoPilot?GameKey={key}&on=true", null);

        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
        var expected = clock.GetUtcNow().AddSeconds(1);
        var state = (await SseClient.GetJsonAsync(client, $"/State?GameKey={key}")).GetProperty("autoPilot");
        Assert.True(state.GetProperty("on").GetBoolean());
        Assert.Equal(expected, state.GetProperty("nextDrawAt").GetDateTimeOffset());
        var pushed = await SseClient.NextMatching(frames, s => s.GetProperty("autoPilot").GetProperty("on").GetBoolean());
        Assert.Equal(expected, pushed.GetProperty("autoPilot").GetProperty("nextDrawAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task AutoPilot_rejects_empty_game_key()
    {
        var (f, _) = Create();
        using var _f = f;
        var response = await f.CreateClient().PostAsync($"/AutoPilot?GameKey={Guid.Empty}&on=true", null);
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Draw_moves_next_draw_at_forward()
    {
        var key = Guid.NewGuid();
        var (f, clock) = Create();
        using var _ = f;
        var client = f.CreateClient();
        await SetAutoPilot(client, key, true);

        clock.Advance(TimeSpan.FromSeconds(5));
        await SseClient.GetJsonAsync(client, $"/Get?GameKey={key}");

        var state = (await SseClient.GetJsonAsync(client, $"/State?GameKey={key}")).GetProperty("autoPilot");
        Assert.True(state.GetProperty("on").GetBoolean());
        Assert.Equal(clock.GetUtcNow().AddSeconds(1), state.GetProperty("nextDrawAt").GetDateTimeOffset());
    }

    [Fact]
    public async Task Draw_without_auto_pilot_leaves_it_off()
    {
        var key = Guid.NewGuid();
        var (f, _) = Create();
        using var _f = f;
        var client = f.CreateClient();
        await SseClient.GetJsonAsync(client, $"/Get?GameKey={key}");
        var state = (await SseClient.GetJsonAsync(client, $"/State?GameKey={key}")).GetProperty("autoPilot");
        Assert.False(state.GetProperty("on").GetBoolean());
    }

    [Fact]
    public async Task AutoPilot_off_clears()
    {
        var key = Guid.NewGuid();
        var (f, _) = Create();
        using var _f = f;
        var client = f.CreateClient();
        await SetAutoPilot(client, key, true);
        await SetAutoPilot(client, key, false);

        var state = (await SseClient.GetJsonAsync(client, $"/State?GameKey={key}")).GetProperty("autoPilot");
        Assert.False(state.GetProperty("on").GetBoolean());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("nextDrawAt").ValueKind);
    }

    [Fact]
    public async Task Reset_turns_auto_pilot_off()
    {
        var key = Guid.NewGuid();
        var (f, _) = Create();
        using var _f = f;
        var client = f.CreateClient();
        await SetAutoPilot(client, key, true);
        await client.PostAsync($"/Reset?GameKey={key}", null);

        var state = (await SseClient.GetJsonAsync(client, $"/State?GameKey={key}")).GetProperty("autoPilot");
        Assert.False(state.GetProperty("on").GetBoolean());
    }

    [Fact]
    public async Task Seventy_fifth_draw_turns_auto_pilot_off()
    {
        var key = Guid.NewGuid();
        var (f, _) = Create();
        using var _f = f;
        var client = f.CreateClient();
        for (var i = 0; i < 74; i++) await SseClient.GetJsonAsync(client, $"/Get?GameKey={key}");
        await SetAutoPilot(client, key, true);
        Assert.True((await SseClient.GetJsonAsync(client, $"/State?GameKey={key}")).GetProperty("autoPilot").GetProperty("on").GetBoolean());

        await SseClient.GetJsonAsync(client, $"/Get?GameKey={key}");

        var state = await SseClient.GetJsonAsync(client, $"/State?GameKey={key}");
        Assert.Equal(75, state.GetProperty("numberOfDrew").GetInt32());
        Assert.False(state.GetProperty("autoPilot").GetProperty("on").GetBoolean());
    }

    [Fact]
    public async Task AutoPilot_on_when_game_over_stays_off()
    {
        var key = Guid.NewGuid();
        var (f, _) = Create();
        using var _f = f;
        var client = f.CreateClient();
        for (var i = 0; i < 75; i++) await SseClient.GetJsonAsync(client, $"/Get?GameKey={key}");

        var response = await client.PostAsync($"/AutoPilot?GameKey={key}&on=true", null);

        Assert.Equal(System.Net.HttpStatusCode.NoContent, response.StatusCode);
        var state = (await SseClient.GetJsonAsync(client, $"/State?GameKey={key}")).GetProperty("autoPilot");
        Assert.False(state.GetProperty("on").GetBoolean());
        Assert.Equal(JsonValueKind.Null, state.GetProperty("nextDrawAt").ValueKind);
    }

    [Fact]
    public async Task Snapshot_server_time_comes_from_clock()
    {
        var key = Guid.NewGuid();
        var (f, clock) = Create();
        using var _ = f;
        var state = await SseClient.GetJsonAsync(f.CreateClient(), $"/State?GameKey={key}");
        Assert.Equal(clock.GetUtcNow(), state.GetProperty("serverTime").GetDateTimeOffset());
    }

    private static async Task<string> NextDrawAtRaw(HttpClient c, Guid key) =>
        (await SseClient.GetJsonAsync(c, $"/State?GameKey={key}")).GetProperty("autoPilot").GetProperty("nextDrawAt").GetString()!;

    private static async Task<int> Drawn(HttpClient c, Guid key) =>
        (await SseClient.GetJsonAsync(c, $"/State?GameKey={key}")).GetProperty("numberOfDrew").GetInt32();

    [Fact]
    public async Task Two_dealers_drawing_the_same_due_tick_draw_one_ball()
    {
        var key = Guid.NewGuid();
        var (f, clock) = Create();
        using var _f = f;
        var client = f.CreateClient();
        await SseClient.GetJsonAsync(client, $"/Get?GameKey={key}");
        await SetAutoPilot(client, key, true);
        var due = Uri.EscapeDataString(await NextDrawAtRaw(client, key));
        clock.Advance(TimeSpan.FromSeconds(1)); // so the draw moves nextDrawAt to a new instant

        var responses = await Task.WhenAll(Enumerable.Range(0, 2)
            .Select(_ => client.GetAsync($"/Get?GameKey={key}&due={due}")));

        Assert.All(responses, r => Assert.Equal(System.Net.HttpStatusCode.OK, r.StatusCode));
        Assert.Equal(2, await Drawn(client, key));
        foreach (var r in responses)
        {
            var body = System.Text.Json.JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;
            Assert.Equal(2, body.GetProperty("numberOfDrew").GetInt32());
            Assert.Equal(2, body.GetProperty("history").EnumerateObject().Count());
        }
    }

    [Fact]
    public async Task A_stale_due_draws_nothing_and_returns_the_current_state()
    {
        var key = Guid.NewGuid();
        var (f, clock) = Create();
        using var _f = f;
        var client = f.CreateClient();
        await SetAutoPilot(client, key, true);
        var stale = Uri.EscapeDataString(await NextDrawAtRaw(client, key));
        clock.Advance(TimeSpan.FromSeconds(1));
        var first = await SseClient.GetJsonAsync(client, $"/Get?GameKey={key}");

        var skipped = await SseClient.GetJsonAsync(client, $"/Get?GameKey={key}&due={stale}");

        Assert.Equal(1, await Drawn(client, key));
        Assert.Equal(1, skipped.GetProperty("numberOfDrew").GetInt32());
        Assert.Equal(first.GetProperty("currentNumber").GetString(), skipped.GetProperty("currentNumber").GetString());
    }

    [Fact]
    public async Task A_due_when_auto_pilot_is_off_draws_nothing()
    {
        var key = Guid.NewGuid();
        var (f, clock) = Create();
        using var _f = f;
        var client = f.CreateClient();
        await SetAutoPilot(client, key, true);
        var due = Uri.EscapeDataString(await NextDrawAtRaw(client, key));
        await SetAutoPilot(client, key, false);

        var skipped = await SseClient.GetJsonAsync(client, $"/Get?GameKey={key}&due={due}");

        Assert.Equal(0, await Drawn(client, key));
        Assert.Equal(0, skipped.GetProperty("numberOfDrew").GetInt32());
        Assert.Equal("", skipped.GetProperty("currentNumber").GetString());
    }

    [Fact]
    public async Task A_matching_due_draws_and_reschedules()
    {
        var key = Guid.NewGuid();
        var (f, clock) = Create();
        using var _f = f;
        var client = f.CreateClient();
        await SetAutoPilot(client, key, true);
        var due = await NextDrawAtRaw(client, key);
        clock.Advance(TimeSpan.FromSeconds(1));

        var drew = await SseClient.GetJsonAsync(client, $"/Get?GameKey={key}&due={Uri.EscapeDataString(due)}");

        Assert.Equal(1, drew.GetProperty("numberOfDrew").GetInt32());
        Assert.NotEqual(due, await NextDrawAtRaw(client, key));
    }

    [Fact]
    public async Task A_draw_without_due_still_draws_while_auto_pilot_is_on()
    {
        var key = Guid.NewGuid();
        var (f, _) = Create();
        using var _f = f;
        var client = f.CreateClient();
        await SetAutoPilot(client, key, true);

        await SseClient.GetJsonAsync(client, $"/Get?GameKey={key}");
        await SseClient.GetJsonAsync(client, $"/Get?GameKey={key}");

        Assert.Equal(2, await Drawn(client, key));
    }
}
