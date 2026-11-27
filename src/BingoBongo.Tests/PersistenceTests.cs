using BingoBongo.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

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
}
