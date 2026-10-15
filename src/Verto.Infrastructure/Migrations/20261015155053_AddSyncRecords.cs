using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Verto.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "sync_version_seq");

            migrationBuilder.CreateTable(
                name: "sync_records",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    deleted_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    client_updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    server_version = table.Column<long>(type: "bigint", nullable: false),
                    received_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_sync_records", x => new { x.user_id, x.entity_type, x.id });
                    table.ForeignKey(
                        name: "FK_sync_records_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_sync_records_user_version",
                table: "sync_records",
                columns: new[] { "user_id", "server_version" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "sync_records");

            migrationBuilder.DropSequence(
                name: "sync_version_seq");
        }
    }
}
