using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildMind.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLandEdgeRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "land_edge_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    project_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    land_revision_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    edge_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    role = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    evidence_source = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    evidence_note = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    reviewed_by = table.Column<Guid>(type: "char(36)", nullable: false),
                    reviewed_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_land_edge_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_land_edge_roles_land_revisions",
                        column: x => x.land_revision_id,
                        principalTable: "land_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_land_edge_roles_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_land_edge_roles_users_reviewed_by",
                        column: x => x.reviewed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_land_edge_roles_project_id",
                table: "land_edge_roles",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_land_edge_roles_reviewed_by",
                table: "land_edge_roles",
                column: "reviewed_by");

            migrationBuilder.CreateIndex(
                name: "ux_land_edge_roles_land_revision_id_edge_id",
                table: "land_edge_roles",
                columns: new[] { "land_revision_id", "edge_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "land_edge_roles");
        }
    }
}
