using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildMind.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveyExtraction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "processing_attempts",
                table: "surveys",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "processing_lease_until_utc",
                table: "surveys",
                type: "datetime(6)",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "survey_extractions",
                columns: table => new
                {
                    extraction_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    survey_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    project_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    outcome = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    method = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    result_json = table.Column<string>(type: "mediumtext", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_extractions", x => x.extraction_id);
                    table.ForeignKey(
                        name: "fk_survey_extractions_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_survey_extractions_surveys",
                        column: x => x.survey_id,
                        principalTable: "surveys",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_surveys_status_lease",
                table: "surveys",
                columns: new[] { "status", "processing_lease_until_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_survey_extractions_project_id",
                table: "survey_extractions",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ux_survey_extractions_survey_id",
                table: "survey_extractions",
                column: "survey_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "survey_extractions");

            migrationBuilder.DropIndex(
                name: "ix_surveys_status_lease",
                table: "surveys");

            migrationBuilder.DropColumn(
                name: "processing_attempts",
                table: "surveys");

            migrationBuilder.DropColumn(
                name: "processing_lease_until_utc",
                table: "surveys");
        }
    }
}
