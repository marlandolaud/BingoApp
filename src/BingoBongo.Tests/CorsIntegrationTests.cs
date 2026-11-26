using Microsoft.AspNetCore.Mvc.Testing;

namespace BingoBongo.Tests;

public class CorsIntegrationTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public CorsIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<HttpResponseMessage> GetGameKeyFrom(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/GetGameKey");
        request.Headers.Add("Origin", origin);
        return await _client.SendAsync(request);
    }

    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("http://192.168.1.118:8080")]
    public async Task Allows_requests_from_ui_on_local_network(string origin)
    {
        var response = await GetGameKeyFrom(origin);

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
        Assert.Equal(origin, Assert.Single(values));
    }

    [Fact]
    public async Task Does_not_allow_requests_from_other_origins()
    {
        var response = await GetGameKeyFrom("http://evil.example.com:8080");

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Allows_reset_preflight_from_local_network()
    {
        var request = new HttpRequestMessage(HttpMethod.Options, "/Reset?GameKey=" + Guid.NewGuid());
        request.Headers.Add("Origin", "http://10.0.0.7:8080");
        request.Headers.Add("Access-Control-Request-Method", "POST");

        var response = await _client.SendAsync(request);

        Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
        Assert.Equal("http://10.0.0.7:8080", Assert.Single(values));
    }
}
