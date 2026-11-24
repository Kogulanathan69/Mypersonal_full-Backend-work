using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildMind.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "surveys",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    project_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    uploaded_by = table.Column<Guid>(type: "char(36)", nullable: false),
                    storage_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    original_file_name = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false),
                    media_type = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    byte_length = table.Column<long>(type: "bigint", nullable: false),
                    sha256_hash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false),
                    uploaded_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    state_version = table.Column<long>(type: "bigint", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    failure_code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    failed_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_surveys", x => x.id);
                    table.ForeignKey(
                        name: "fk_surveys_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_surveys_users_uploaded_by",
                        column: x => x.uploaded_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_surveys_project_id_uploaded_at_utc",
                table: "surveys",
                columns: new[] { "project_id", "uploaded_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_surveys_uploaded_by",
                table: "surveys",
                column: "uploaded_by");

            migrationBuilder.CreateIndex(
                name: "ux_surveys_storage_id",
                table: "surveys",
                column: "storage_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "surveys");
        }
    }
}
