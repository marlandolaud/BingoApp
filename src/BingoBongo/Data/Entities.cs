namespace BingoBongo.Data;

public class GameEntity
{
    public Guid Id { get; set; }
    public Guid JoinCode { get; set; }
    public int Round { get; set; }
    public DateTimeOffset? AutoPilotNextDrawAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

public class DrawEntity
{
    public Guid GameId { get; set; }
    public int Seq { get; set; }
    public string Ball { get; set; } = "";
}

public class ViewerEntity
{
    public Guid Id { get; set; }
    public Guid GameId { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastSeenAt { get; set; }
}
