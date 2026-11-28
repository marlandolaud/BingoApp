using BingoBongo.Live;
using Npgsql;

namespace BingoBongo.Tests;

[Trait("Category", "Unit")]
public class PgNotificationListenerTests
{
    private const string Base = "Host=db;Database=bingo;Username=postgres;Password=x";

    [Fact]
    public void Listener_connection_sends_keepalives_so_a_half_open_connection_is_detected() =>
        Assert.Equal(30, new NpgsqlConnectionStringBuilder(PgNotificationListener.ListenerConnectionString(Base, "app")).KeepAlive);

    [Fact]
    public void Listener_connection_is_named_and_keeps_the_base_settings()
    {
        var cs = new NpgsqlConnectionStringBuilder(PgNotificationListener.ListenerConnectionString(Base, "bingo-listener-1"));
        Assert.Equal("bingo-listener-1", cs.ApplicationName);
        Assert.Equal("db", cs.Host);
        Assert.Equal("bingo", cs.Database);
    }
}
