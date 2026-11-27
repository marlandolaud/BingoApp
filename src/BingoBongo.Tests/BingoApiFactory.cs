using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BingoBongo.Tests;

public class BingoApiFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Bingo", PostgresFixture.ConnectionString);
        builder.UseSetting("AutoPilot:IntervalSeconds", "1");
        builder.UseSetting("Live:HeartbeatSeconds", "1");
    }
}
