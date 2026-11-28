using Microsoft.EntityFrameworkCore;

namespace BingoBongo.Data;

public class BingoDbContext(DbContextOptions<BingoDbContext> options) : DbContext(options)
{
    public DbSet<GameEntity> Games => Set<GameEntity>();
    public DbSet<DrawEntity> Draws => Set<DrawEntity>();
    public DbSet<ViewerEntity> Viewers => Set<ViewerEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<GameEntity>(e =>
        {
            e.ToTable("games");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.JoinCode).HasColumnName("join_code");
            e.Property(x => x.Round).HasColumnName("round");
            e.Property(x => x.AutoPilotNextDrawAt).HasColumnName("auto_pilot_next_draw_at");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
            e.HasIndex(x => x.JoinCode).IsUnique().HasDatabaseName("ix_games_join_code");
        });

        modelBuilder.Entity<DrawEntity>(e =>
        {
            e.ToTable("draws");
            e.HasKey(x => new { x.GameId, x.Seq });
            e.Property(x => x.GameId).HasColumnName("game_id");
            e.Property(x => x.Seq).HasColumnName("seq");
            e.Property(x => x.Ball).HasColumnName("ball");
            e.HasIndex(x => new { x.GameId, x.Ball }).IsUnique().HasDatabaseName("ix_draws_game_id_ball");
            e.HasOne<GameEntity>().WithMany().HasForeignKey(x => x.GameId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ViewerEntity>(e =>
        {
            e.ToTable("viewers");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).HasColumnName("id");
            e.Property(x => x.GameId).HasColumnName("game_id");
            e.Property(x => x.CreatedAt).HasColumnName("created_at");
            e.Property(x => x.LastSeenAt).HasColumnName("last_seen_at");
            e.HasIndex(x => new { x.GameId, x.LastSeenAt }).HasDatabaseName("ix_viewers_game_id_last_seen_at");
        });
    }
}
