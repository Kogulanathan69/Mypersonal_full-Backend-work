using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildMind.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSurveyExtractionReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "survey_extraction_reviews",
                columns: table => new
                {
                    review_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    extraction_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    survey_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    project_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    version = table.Column<int>(type: "int", nullable: false),
                    reviewed_by = table.Column<Guid>(type: "char(36)", nullable: false),
                    decisions_json = table.Column<string>(type: "mediumtext", nullable: false),
                    note = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_survey_extraction_reviews", x => x.review_id);
                    table.ForeignKey(
                        name: "fk_survey_extraction_reviews_extractions",
                        column: x => x.extraction_id,
                        principalTable: "survey_extractions",
                        principalColumn: "extraction_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_survey_extraction_reviews_projects",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_survey_extraction_reviews_users",
                        column: x => x.reviewed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_survey_extraction_reviews_project_id",
                table: "survey_extraction_reviews",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_survey_extraction_reviews_reviewed_by",
                table: "survey_extraction_reviews",
                column: "reviewed_by");

            migrationBuilder.CreateIndex(
                name: "ux_survey_extraction_reviews_version",
                table: "survey_extraction_reviews",
                columns: new[] { "extraction_id", "version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "survey_extraction_reviews");
        }
    }
}
