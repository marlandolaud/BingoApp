using System.Text;
using System.Text.Json;
using BingoBongo.Live;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace BingoBongo.Tests;

public class LiveEventsTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(5);

    [Fact]
    public async Task Events_sends_snapshot_on_connect()
    {
        using var f = new BingoApiFactory();
        using var cts = new CancellationTokenSource(Timeout);
        using var response = await SseClient.OpenAsync(f.CreateClient(), $"/Events?GameKey={Guid.NewGuid()}", cts.Token);

        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("no", Assert.Single(response.Headers.GetValues("X-Accel-Buffering")));
        await using var frames = SseClient.ReadStatesAsync(response, cts.Token).GetAsyncEnumerator(cts.Token);
        var first = await Next(frames);
        Assert.Equal(0, first.GetProperty("numberOfDrew").GetInt32());
    }

    [Fact]
    public async Task Events_rejects_empty_game_key()
    {
        using var f = new BingoApiFactory();
        var response = await f.CreateClient().GetAsync($"/Events?GameKey={Guid.Empty}");

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Events_pushes_snapshot_after_draw()
    {
        var key = Guid.NewGuid();
        using var f = new BingoApiFactory();
        var client = f.CreateClient();
        using var cts = new CancellationTokenSource(Timeout);
        await using var frames = SseClient.ReadStatesAsync(client, $"/Events?GameKey={key}", cts.Token).GetAsyncEnumerator(cts.Token);
        await Next(frames);

        var draw = await SseClient.GetJsonAsync(client, $"/Get?GameKey={key}");

        var next = await SseClient.NextMatching(frames, s => s.GetProperty("numberOfDrew").GetInt32() == 1);
        Assert.Equal(draw.GetProperty("currentNumber").GetString(), next.GetProperty("currentNumber").GetString());
    }

    [Fact]
    public async Task Reset_keeps_stream_open_and_pushes_cleared_board()
    {
        var key = Guid.NewGuid();
        using var f = new BingoApiFactory();
        var client = f.CreateClient();
        using var cts = new CancellationTokenSource(Timeout * 2);
        await using var frames = SseClient.ReadStatesAsync(client, $"/Events?GameKey={key}", cts.Token).GetAsyncEnumerator(cts.Token);
        await Next(frames);

        await SseClient.GetJsonAsync(client, $"/Get?GameKey={key}");
        await SseClient.NextMatching(frames, s => s.GetProperty("numberOfDrew").GetInt32() == 1);

        (await client.PostAsync($"/Reset?GameKey={key}", null)).EnsureSuccessStatusCode();
        var cleared = await SseClient.NextMatching(frames, s => s.GetProperty("numberOfDrew").GetInt32() == 0);
        Assert.Equal(2, cleared.GetProperty("round").GetInt32());
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("currentNumber").ValueKind);
        Assert.Empty(cleared.GetProperty("history").EnumerateObject());

        var draw = await SseClient.GetJsonAsync(client, $"/Get?GameKey={key}");
        var after = await SseClient.NextMatching(frames, s => s.GetProperty("numberOfDrew").GetInt32() == 1);
        Assert.Equal(draw.GetProperty("currentNumber").GetString(), after.GetProperty("currentNumber").GetString());
    }

    [Fact]
    public async Task Draw_on_one_instance_reaches_subscriber_on_another()
    {
        var key = Guid.NewGuid();
        using var a = new BingoApiFactory();
        using var b = new BingoApiFactory();
        var clientA = a.CreateClient();
        using var cts = new CancellationTokenSource(Timeout);
        await using var frames = SseClient.ReadStatesAsync(b.CreateClient(), $"/Events?GameKey={key}", cts.Token).GetAsyncEnumerator(cts.Token);
        await Next(frames);

        await SseClient.GetJsonAsync(clientA, $"/Get?GameKey={key}");

        await SseClient.NextMatching(frames, s => s.GetProperty("numberOfDrew").GetInt32() == 1);
    }

    [Fact]
    public async Task Listener_reconnects_after_losing_its_connection()
    {
        var key = Guid.NewGuid();
        using var f = new BingoApiFactory();
        var client = f.CreateClient();
        using var cts = new CancellationTokenSource(Timeout);
        await using var frames = SseClient.ReadStatesAsync(client, $"/Events?GameKey={key}", cts.Token).GetAsyncEnumerator(cts.Token);
        await Next(frames);

        // Kill only this factory's listener, so parallel test classes are unaffected.
        var listener = f.Services.GetServices<IHostedService>().OfType<PgNotificationListener>().Single();
        await using (var admin = new NpgsqlConnection(PostgresFixture.ConnectionString))
        {
            await admin.OpenAsync();
            const string sql = """
                SELECT count(pg_terminate_backend(pid)) FROM pg_stat_activity
                WHERE application_name = @name AND query = 'LISTEN game_events'
                """;
            long killed = 0;
            // The listener connects in the background; retry until its LISTEN backend exists.
            while (killed == 0)
            {
                cts.Token.ThrowIfCancellationRequested();
                await using var kill = new NpgsqlCommand(sql, admin);
                kill.Parameters.AddWithValue("name", listener.ApplicationName);
                killed = (long)(await kill.ExecuteScalarAsync(cts.Token))!;
                if (killed == 0) await Task.Delay(50, cts.Token);
            }
            Assert.Equal(1, killed);
        }
        // The draw may land before or after LISTEN is re-established; the reconnect's
        // PublishAll covers the first case and the notification the second.
        await SseClient.GetJsonAsync(client, $"/Get?GameKey={key}");

        await SseClient.NextMatching(frames, s => s.GetProperty("numberOfDrew").GetInt32() == 1);
    }

    [Fact]
    public async Task Events_sends_heartbeat_comment()
    {
        using var f = new BingoApiFactory();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        using var response = await SseClient.OpenAsync(f.CreateClient(), $"/Events?GameKey={Guid.NewGuid()}", cts.Token);
        await using var stream = await response.Content.ReadAsStreamAsync(cts.Token);

        var text = new StringBuilder();
        var buffer = new byte[1024];
        while (!text.ToString().Contains(": ping\n\n"))
        {
            var read = await stream.ReadAsync(buffer, cts.Token);
            Assert.NotEqual(0, read);
            text.Append(Encoding.UTF8.GetString(buffer, 0, read));
        }
    }

    private static async Task<JsonElement> Next(IAsyncEnumerator<JsonElement> frames)
    {
        Assert.True(await frames.MoveNextAsync(), "stream ended");
        return frames.Current;
    }
}
