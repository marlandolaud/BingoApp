using Npgsql;

namespace BingoBongo.Live;

/// <summary>
/// Holds a dedicated Postgres connection on <c>LISTEN game_events</c> and turns each notification
/// (payload = game id) into a hub publish, so changes made by any API instance reach local streams.
/// </summary>
public class PgNotificationListener(
    IConfiguration configuration,
    GameEventHub hub,
    ILogger<PgNotificationListener> logger) : BackgroundService
{
    public const string Channel = "game_events";
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(1);

    /// <summary>Unique per instance, so this listener's backend is identifiable in pg_stat_activity.</summary>
    public string ApplicationName { get; } = $"bingo-listener-{Guid.NewGuid():N}";

    /// <summary>
    /// The LISTEN connection: named, and with keepalives so a half-open connection (the peer
    /// vanished without closing it) fails <c>WaitAsync</c> and the loop reconnects.
    /// </summary>
    public static string ListenerConnectionString(string? baseConnectionString, string applicationName) =>
        new NpgsqlConnectionStringBuilder(baseConnectionString)
        {
            ApplicationName = applicationName,
            KeepAlive = 30,
        }.ConnectionString;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connectionString = ListenerConnectionString(configuration.GetConnectionString("Bingo"), ApplicationName);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var conn = new NpgsqlConnection(connectionString);
                conn.Notification += OnNotification;
                await conn.OpenAsync(stoppingToken);
                await using (var cmd = new NpgsqlCommand($"LISTEN {Channel}", conn))
                {
                    await cmd.ExecuteNonQueryAsync(stoppingToken);
                }

                // Anything that changed before LISTEN took effect (startup or a dropped
                // connection) produced no notification here, so have every stream reload.
                hub.PublishAll();

                while (true)
                {
                    await conn.WaitAsync(stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Lost the {Channel} listener connection; reconnecting", Channel);
                try
                {
                    await Task.Delay(RetryDelay, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private void OnNotification(object sender, NpgsqlNotificationEventArgs e)
    {
        if (Guid.TryParse(e.Payload, out var gameId))
        {
            hub.Publish(gameId);
        }
    }
}
