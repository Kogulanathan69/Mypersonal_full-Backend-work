using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildMind.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCaptainDeliverables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reports",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    project_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    requested_by = table.Column<Guid>(type: "char(36)", nullable: false),
                    requested_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    snapshot_schema_version = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    captured_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    requirements_revision_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    plan_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    plan_revision_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    confirmation_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    disclaimer_version = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    disclaimer_text = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false),
                    state_version = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    artifact_id = table.Column<Guid>(type: "char(36)", nullable: true),
                    artifact_media_type = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    artifact_byte_length = table.Column<long>(type: "bigint", nullable: true),
                    artifact_content_hash = table.Column<string>(type: "varchar(71)", maxLength: 71, nullable: true),
                    artifact_created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    completed_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    failure_code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    failed_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reports", x => x.id);
                    table.ForeignKey(
                        name: "fk_reports_plan_confirmations",
                        column: x => x.confirmation_id,
                        principalTable: "plan_confirmations",
                        principalColumn: "confirmation_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reports_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reports_users_requested_by",
                        column: x => x.requested_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "architect_requests",
                columns: table => new
                {
                    request_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    consent_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    project_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    granted_by = table.Column<Guid>(type: "char(36)", nullable: false),
                    recipient_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    report_id = table.Column<Guid>(type: "char(36)", nullable: true),
                    document_ids_json = table.Column<string>(type: "text", nullable: false),
                    policy_version = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    granted_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    requested_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    state_version = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    withdrawn_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_architect_requests", x => x.request_id);
                    table.ForeignKey(
                        name: "fk_architect_requests_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_architect_requests_reports",
                        column: x => x.report_id,
                        principalTable: "reports",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_architect_requests_users_granted_by",
                        column: x => x.granted_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_architect_requests_granted_by",
                table: "architect_requests",
                column: "granted_by");

            migrationBuilder.CreateIndex(
                name: "ix_architect_requests_project_requested",
                table: "architect_requests",
                columns: new[] { "project_id", "requested_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_architect_requests_report_id",
                table: "architect_requests",
                column: "report_id");

            migrationBuilder.CreateIndex(
                name: "ux_architect_requests_consent_id",
                table: "architect_requests",
                column: "consent_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_reports_confirmation_id",
                table: "reports",
                column: "confirmation_id");

            migrationBuilder.CreateIndex(
                name: "ix_reports_project_requested",
                table: "reports",
                columns: new[] { "project_id", "requested_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_reports_requested_by",
                table: "reports",
                column: "requested_by");

            migrationBuilder.CreateIndex(
                name: "ux_reports_artifact_id",
                table: "reports",
                column: "artifact_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "architect_requests");

            migrationBuilder.DropTable(
                name: "reports");
        }
    }
}
