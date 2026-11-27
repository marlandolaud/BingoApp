using System.Runtime.CompilerServices;
using BingoBongo.Data;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace BingoBongo.Tests;

/// <summary>
/// Starts one throwaway Postgres container for the whole test run and points the app at it
/// through the ConnectionStrings__Bingo environment variable.
/// </summary>
public static class PostgresFixture
{
    public static string ConnectionString { get; private set; } = "";

    [ModuleInitializer]
    public static void Start()
    {
        // Unit-only runs (mutation testing) never touch the database; skip the container.
        if (Environment.GetEnvironmentVariable("BINGO_SKIP_DB") == "1") return;
        var container = new PostgreSqlBuilder("postgres:17-alpine").Build();
        container.StartAsync().GetAwaiter().GetResult();
        ConnectionString = container.GetConnectionString();
        Environment.SetEnvironmentVariable("ConnectionStrings__Bingo", ConnectionString);
        // Migrate once up front so parallel test classes starting the app don't race on the DDL.
        var options = new DbContextOptionsBuilder<BingoDbContext>().UseNpgsql(ConnectionString).Options;
        using (var db = new BingoDbContext(options))
        {
            db.Database.Migrate();
        }
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            container.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
