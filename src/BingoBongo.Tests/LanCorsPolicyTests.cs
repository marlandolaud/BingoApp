namespace BingoBongo.Tests;

public class LanCorsPolicyTests
{
    [Theory]
    [InlineData("http://localhost:8080")]
    [InlineData("http://127.0.0.1:8080")]
    [InlineData("http://[::1]:8080")]
    [InlineData("http://192.168.1.118:8080")]
    [InlineData("http://192.168.0.1:8080")]
    [InlineData("http://10.0.0.5:8080")]
    [InlineData("http://10.255.255.255:8080")]
    [InlineData("http://172.16.0.1:8080")]
    [InlineData("http://172.31.255.254:8080")]
    [InlineData("http://[fe80::1]:8080")]
    [InlineData("http://[fd12:3456::1]:8080")]
    [InlineData("https://192.168.1.118:8080")]
    public void Allows_ui_on_this_machine_or_private_network(string origin)
    {
        Assert.True(LanCorsPolicy.IsAllowedOrigin(origin));
    }

    [Theory]
    [InlineData("http://192.168.1.118:3000")]   // wrong port
    [InlineData("http://localhost")]            // default port 80
    [InlineData("http://8.8.8.8:8080")]         // public IPv4
    [InlineData("http://172.15.0.1:8080")]      // just below 172.16/12
    [InlineData("http://172.32.0.1:8080")]      // just above 172.16/12
    [InlineData("http://192.169.0.1:8080")]     // not 192.168/16
    [InlineData("http://11.0.0.1:8080")]        // not 10/8
    [InlineData("http://[2600:1700::1]:8080")]  // public IPv6
    [InlineData("http://evil.example.com:8080")]
    [InlineData("ftp://192.168.1.118:8080")]
    [InlineData("not a url")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_everything_else(string? origin)
    {
        Assert.False(LanCorsPolicy.IsAllowedOrigin(origin));
    }
}
