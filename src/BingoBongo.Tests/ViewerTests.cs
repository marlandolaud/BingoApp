using System.Net;
using System.Text.Json;
using Npgsql;

namespace BingoBongo.Tests;

public class ViewerTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(8);

    private static async Task<Guid> JoinCodeOf(HttpClient client, Guid key)
    {
        var state = await SseClient.GetJsonAsync(client, $"/State?GameKey={key}");
        return state.GetProperty("joinCode").GetGuid();
    }

    private static async Task<Guid> JoinAsync(HttpClient client, Guid joinCode)
    {
        var response = await client.PostAsync($"/viewers?join={joinCode}", null);
        response.EnsureSuccessStatusCode();
        var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        return json.GetProperty("viewerId").GetGuid();
    }

    [Fact]
    public async Task Each_join_gets_a_distinct_viewer_id()
    {
        var key = Guid.NewGuid();
        using var f = new BingoApiFactory();
        var client = f.CreateClient();
        var code = await JoinCodeOf(client, key);

        var ids = new[] { await JoinAsync(client, code), await JoinAsync(client, code), await JoinAsync(client, code) };

        Assert.Equal(3, ids.Distinct().Count());
    }

    [Fact]
    public async Task Unknown_join_code_is_404()
    {
        using var f = new BingoApiFactory();

        var response = await f.CreateClient().PostAsync($"/viewers?join={Guid.NewGuid()}", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Unknown_viewer_is_404_for_state_and_events()
    {
        using var f = new BingoApiFactory();
        var client = f.CreateClient();
        var id = Guid.NewGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/watch/{id}/state")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/watch/{id}/events")).StatusCode);
    }

    [Fact]
    public async Task Viewer_state_matches_game_and_hides_dealer_secrets()
    {
        var key = Guid.NewGuid();
        using var f = new BingoApiFactory();
        var client = f.CreateClient();
        var code = await JoinCodeOf(client, key);
        var viewer = await JoinAsync(client, code);
        await SseClient.GetJsonAsync(client, $"/Get?GameKey={key}");
        await SseClient.GetJsonAsync(client, $"/Get?GameKey={key}");

        var response = await client.GetAsync($"/watch/{viewer}/state");
        response.EnsureSuccessStatusCode();
        var raw = await response.Content.ReadAsStringAsync();
        var state = JsonDocument.Parse(raw).RootElement;
        var dealer = await SseClient.GetJsonAsync(client, $"/State?GameKey={key}");

        Assert.Equal(2, state.GetProperty("numberOfDrew").GetInt32());
        Assert.Equal(dealer.GetProperty("history").GetRawText(), state.GetProperty("history").GetRawText());
        Assert.DoesNotContain(key.ToString(), raw, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(JsonValueKind.Null, state.GetProperty("joinCode").ValueKind);
    }

    [Fact]
    public async Task Viewer_stream_follows_draws_and_reset()
    {
        var key = Guid.NewGuid();
        using var f = new BingoApiFactory();
        var client = f.CreateClient();
        var viewer = await JoinAsync(client, await JoinCodeOf(client, key));
        using var cts = new CancellationTokenSource(Timeout);
        using var response = await SseClient.OpenAsync(client, $"/watch/{viewer}/events", cts.Token);
        await using var frames = SseClient.ReadStatesAsync(response, cts.Token).GetAsyncEnumerator(cts.Token);
        await SseClient.NextMatching(frames, s => s.GetProperty("numberOfDrew").GetInt32() == 0);

        var draw = await SseClient.GetJsonAsync(client, $"/Get?GameKey={key}");
        var drawn = await SseClient.NextMatching(frames, s => s.GetProperty("numberOfDrew").GetInt32() == 1);
        Assert.Equal(draw.GetProperty("currentNumber").GetString(), drawn.GetProperty("currentNumber").GetString());
        Assert.Equal(JsonValueKind.Null, drawn.GetProperty("joinCode").ValueKind);
        Assert.DoesNotContain(key.ToString(), drawn.GetRawText(), StringComparison.OrdinalIgnoreCase);

        (await client.PostAsync($"/Reset?GameKey={key}", null)).EnsureSuccessStatusCode();
        var cleared = await SseClient.NextMatching(frames, s => s.GetProperty("numberOfDrew").GetInt32() == 0);
        Assert.Equal(2, cleared.GetProperty("round").GetInt32());
    }

    [Fact]
    public async Task Viewer_count_rises_on_connect_and_falls_on_disconnect()
    {
        var key = Guid.NewGuid();
        using var f = new BingoApiFactory();
        var client = f.CreateClient();
        var viewer = await JoinAsync(client, await JoinCodeOf(client, key));
        using var cts = new CancellationTokenSource(Timeout);
        await using var dealer = SseClient.ReadStatesAsync(client, $"/Events?GameKey={key}", cts.Token).GetAsyncEnumerator(cts.Token);
        await SseClient.NextMatching(dealer, s => s.GetProperty("viewerCount").GetInt32() == 0);

        using var viewerCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token);
        using var response = await SseClient.OpenAsync(client, $"/watch/{viewer}/events", viewerCts.Token);
        await using var frames = SseClient.ReadStatesAsync(response, viewerCts.Token).GetAsyncEnumerator(viewerCts.Token);
        await frames.MoveNextAsync();
        await SseClient.NextMatching(dealer, s => s.GetProperty("viewerCount").GetInt32() == 1);

        await viewerCts.CancelAsync();
        response.Dispose();
        await SseClient.NextMatching(dealer, s => s.GetProperty("viewerCount").GetInt32() == 0);
    }

    [Fact]
    public async Task Viewer_id_used_as_game_key_cannot_touch_real_game()
    {
        var key = Guid.NewGuid();
        using var f = new BingoApiFactory();
        var client = f.CreateClient();
        var viewer = await JoinAsync(client, await JoinCodeOf(client, key));
        await SseClient.GetJsonAsync(client, $"/Get?GameKey={key}");

        await client.GetAsync($"/Get?GameKey={viewer}");

        var dealer = await SseClient.GetJsonAsync(client, $"/State?GameKey={key}");
        Assert.Equal(1, dealer.GetProperty("numberOfDrew").GetInt32());
    }

    [Fact]
    public async Task Stale_viewer_is_not_counted()
    {
        var key = Guid.NewGuid();
        using var f = new BingoApiFactory();
        var client = f.CreateClient();
        var viewer = await JoinAsync(client, await JoinCodeOf(client, key));

        await SetLastSeen(viewer, DateTimeOffset.UtcNow);
        var fresh = await SseClient.GetJsonAsync(client, $"/State?GameKey={key}");
        await SetLastSeen(viewer, DateTimeOffset.UtcNow - TimeSpan.FromSeconds(3));
        var stale = await SseClient.GetJsonAsync(client, $"/State?GameKey={key}");

        Assert.Equal(1, fresh.GetProperty("viewerCount").GetInt32());
        Assert.Equal(0, stale.GetProperty("viewerCount").GetInt32());
    }

    private static async Task SetLastSeen(Guid viewer, DateTimeOffset at)
    {
        await using var conn = new NpgsqlConnection(PostgresFixture.ConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("UPDATE viewers SET last_seen_at = @at WHERE id = @id", conn);
        cmd.Parameters.AddWithValue("at", at);
        cmd.Parameters.AddWithValue("id", viewer);
        await cmd.ExecuteNonQueryAsync();
    }
}
