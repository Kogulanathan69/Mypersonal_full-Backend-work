using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildMind.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCaptainPlanningWorkflow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "land_revisions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    project_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    revision_number = table.Column<long>(type: "bigint", nullable: false),
                    land_json = table.Column<string>(type: "mediumtext", nullable: false),
                    area_m2 = table.Column<double>(type: "double", nullable: false),
                    edge_count = table.Column<int>(type: "int", nullable: false),
                    survey_id = table.Column<Guid>(type: "char(36)", nullable: true),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_land_revisions", x => x.id);
                    table.ForeignKey(
                        name: "fk_land_revisions_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_land_revisions_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "plan_validation_evidence",
                columns: table => new
                {
                    validation_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    project_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    plan_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    validator_version = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    geometry_hash = table.Column<string>(type: "varchar(71)", maxLength: 71, nullable: false),
                    geometry_valid = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    requirements_match = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    regulatory_evidence_status = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    has_blocking_issues = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    validated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    diagnostics_json = table.Column<string>(type: "mediumtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_validation_evidence", x => x.validation_id);
                    table.ForeignKey(
                        name: "fk_plan_validation_evidence_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "generation_jobs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    project_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    requested_by = table.Column<Guid>(type: "char(36)", nullable: false),
                    idempotency_key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    land_revision_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    requirements_revision_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    outcome = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: true),
                    reason_code = table.Column<string>(type: "varchar(80)", maxLength: 80, nullable: true),
                    algorithm_version = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    evidence_mode = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    seed = table.Column<int>(type: "int", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    completed_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    diagnostics_json = table.Column<string>(type: "mediumtext", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_generation_jobs", x => x.id);
                    table.ForeignKey(
                        name: "fk_generation_jobs_land_revisions",
                        column: x => x.land_revision_id,
                        principalTable: "land_revisions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_generation_jobs_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_generation_jobs_users_requested_by",
                        column: x => x.requested_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "generation_candidates",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    job_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    project_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    ordinal = table.Column<int>(type: "int", nullable: false),
                    geometry_json = table.Column<string>(type: "mediumtext", nullable: false),
                    geometry_hash = table.Column<string>(type: "varchar(71)", maxLength: 71, nullable: false),
                    metrics_json = table.Column<string>(type: "text", nullable: false),
                    validation_json = table.Column<string>(type: "mediumtext", nullable: false),
                    diversity_signature = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_generation_candidates", x => x.id);
                    table.ForeignKey(
                        name: "fk_generation_candidates_generation_jobs",
                        column: x => x.job_id,
                        principalTable: "generation_jobs",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_generation_candidates_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_generation_candidates_project_id",
                table: "generation_candidates",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ux_generation_candidates_job_ordinal",
                table: "generation_candidates",
                columns: new[] { "job_id", "ordinal" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_generation_jobs_land_revision_id",
                table: "generation_jobs",
                column: "land_revision_id");

            migrationBuilder.CreateIndex(
                name: "ix_generation_jobs_requested_by",
                table: "generation_jobs",
                column: "requested_by");

            migrationBuilder.CreateIndex(
                name: "ux_generation_jobs_project_actor_key",
                table: "generation_jobs",
                columns: new[] { "project_id", "requested_by", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_land_revisions_created_by",
                table: "land_revisions",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ux_land_revisions_project_id_revision_number",
                table: "land_revisions",
                columns: new[] { "project_id", "revision_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plan_validation_evidence_project_plan",
                table: "plan_validation_evidence",
                columns: new[] { "project_id", "plan_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "generation_candidates");

            migrationBuilder.DropTable(
                name: "plan_validation_evidence");

            migrationBuilder.DropTable(
                name: "generation_jobs");

            migrationBuilder.DropTable(
                name: "land_revisions");
        }
    }
}
