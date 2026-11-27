using System.Runtime.CompilerServices;
using System.Text.Json;

namespace BingoBongo.Tests;

/// <summary>Minimal Server-Sent Events reader for tests.</summary>
public static class SseClient
{
    /// <summary>Opens the stream and yields the JSON payload of each <c>event: state</c> frame.</summary>
    public static async IAsyncEnumerable<JsonElement> ReadStatesAsync(
        HttpClient client, string url, [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var response = await OpenAsync(client, url, ct);
        await foreach (var state in ReadStatesAsync(response, ct))
        {
            yield return state;
        }
    }

    public static async Task<HttpResponseMessage> OpenAsync(HttpClient client, string url, CancellationToken ct = default)
    {
        var response = await client.SendAsync(
            new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();
        return response;
    }

    public static async IAsyncEnumerable<JsonElement> ReadStatesAsync(
        HttpResponseMessage response, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var reader = new StreamReader(stream);
        string? eventName = null;
        while (await reader.ReadLineAsync(ct) is { } line)
        {
            if (line.StartsWith("event: "))
            {
                eventName = line["event: ".Length..];
            }
            else if (line.StartsWith("data: ") && eventName == "state")
            {
                yield return JsonDocument.Parse(line["data: ".Length..]).RootElement.Clone();
            }
            else if (line.Length == 0)
            {
                eventName = null;
            }
        }
    }
}
