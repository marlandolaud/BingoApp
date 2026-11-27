using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BingoBongo.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "games",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    join_code = table.Column<Guid>(type: "uuid", nullable: false),
                    round = table.Column<int>(type: "integer", nullable: false),
                    auto_pilot_next_draw_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_games", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "viewers",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_viewers", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "draws",
                columns: table => new
                {
                    game_id = table.Column<Guid>(type: "uuid", nullable: false),
                    seq = table.Column<int>(type: "integer", nullable: false),
                    ball = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_draws", x => new { x.game_id, x.seq });
                    table.ForeignKey(
                        name: "FK_draws_games_game_id",
                        column: x => x.game_id,
                        principalTable: "games",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_draws_game_id_ball",
                table: "draws",
                columns: new[] { "game_id", "ball" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_games_join_code",
                table: "games",
                column: "join_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_viewers_game_id_last_seen_at",
                table: "viewers",
                columns: new[] { "game_id", "last_seen_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "draws");

            migrationBuilder.DropTable(
                name: "viewers");

            migrationBuilder.DropTable(
                name: "games");
        }
    }
}
