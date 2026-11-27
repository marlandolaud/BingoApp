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

    private static async Task<JsonElement> Json(HttpClient c, string url)
    {
        var r = await c.GetAsync(url);
        r.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private static async Task<JsonElement> NextMatching(
        IAsyncEnumerator<JsonElement> frames, Func<JsonElement, bool> predicate)
    {
        while (await frames.MoveNextAsync())
        {
            if (predicate(frames.Current)) return frames.Current;
        }
        throw new InvalidOperationException("Stream ended");
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
        var state = (await Json(client, $"/State?GameKey={key}")).GetProperty("autoPilot");
        Assert.True(state.GetProperty("on").GetBoolean());
        Assert.Equal(expected, state.GetProperty("nextDrawAt").GetDateTimeOffset());
        var pushed = await NextMatching(frames, s => s.GetProperty("autoPilot").GetProperty("on").GetBoolean());
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
        await Json(client, $"/Get?GameKey={key}");

        var state = (await Json(client, $"/State?GameKey={key}")).GetProperty("autoPilot");
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
        await Json(client, $"/Get?GameKey={key}");
        var state = (await Json(client, $"/State?GameKey={key}")).GetProperty("autoPilot");
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

        var state = (await Json(client, $"/State?GameKey={key}")).GetProperty("autoPilot");
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

        var state = (await Json(client, $"/State?GameKey={key}")).GetProperty("autoPilot");
        Assert.False(state.GetProperty("on").GetBoolean());
    }

    [Fact]
    public async Task Seventy_fifth_draw_turns_auto_pilot_off()
    {
        var key = Guid.NewGuid();
        var (f, _) = Create();
        using var _f = f;
        var client = f.CreateClient();
        for (var i = 0; i < 74; i++) await Json(client, $"/Get?GameKey={key}");
        await SetAutoPilot(client, key, true);
        Assert.True((await Json(client, $"/State?GameKey={key}")).GetProperty("autoPilot").GetProperty("on").GetBoolean());

        await Json(client, $"/Get?GameKey={key}");

        var state = await Json(client, $"/State?GameKey={key}");
        Assert.Equal(75, state.GetProperty("numberOfDrew").GetInt32());
        Assert.False(state.GetProperty("autoPilot").GetProperty("on").GetBoolean());
    }

    [Fact]
    public async Task Snapshot_server_time_comes_from_clock()
    {
        var key = Guid.NewGuid();
        var (f, clock) = Create();
        using var _ = f;
        var state = await Json(f.CreateClient(), $"/State?GameKey={key}");
        Assert.Equal(clock.GetUtcNow(), state.GetProperty("serverTime").GetDateTimeOffset());
    }
}
