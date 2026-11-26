using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildMind.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialFeatureSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false),
                    normalized_email = table.Column<string>(type: "varchar(254)", maxLength: 254, nullable: false),
                    password_hash = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    full_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    phone_number = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_users", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "projects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    owner_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    name = table.Column<string>(type: "varchar(150)", maxLength: 150, nullable: false),
                    project_type = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: true),
                    location_reference = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    status = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    revision = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_projects", x => x.id);
                    table.ForeignKey(
                        name: "fk_projects_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "requirement_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    project_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    revision_number = table.Column<long>(type: "bigint", nullable: false),
                    schema_version = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    catalogue_version = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    content_json = table.Column<string>(type: "json", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requirement_revisions", x => x.id);
                    table.UniqueConstraint("ak_requirement_revisions_project_id_id", x => new { x.project_id, x.id });
                    table.ForeignKey(
                        name: "fk_requirement_revisions_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "project_requirement_heads",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    current_revision_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    current_revision_number = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_project_requirement_heads", x => x.project_id);
                    table.ForeignKey(
                        name: "fk_project_requirement_heads_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_project_requirement_heads_requirement_revisions_current",
                        columns: x => new { x.project_id, x.current_revision_id },
                        principalTable: "requirement_revisions",
                        principalColumns: new[] { "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_project_requirement_heads_project_id_current_revision_id",
                table: "project_requirement_heads",
                columns: new[] { "project_id", "current_revision_id" });

            migrationBuilder.CreateIndex(
                name: "ix_projects_owner_id_created_at_utc",
                table: "projects",
                columns: new[] { "owner_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ux_requirement_revisions_project_id_revision_number",
                table: "requirement_revisions",
                columns: new[] { "project_id", "revision_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_users_normalized_email",
                table: "users",
                column: "normalized_email",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "project_requirement_heads");

            migrationBuilder.DropTable(
                name: "requirement_revisions");

            migrationBuilder.DropTable(
                name: "projects");

            migrationBuilder.DropTable(
                name: "users");
        }
    }
}
