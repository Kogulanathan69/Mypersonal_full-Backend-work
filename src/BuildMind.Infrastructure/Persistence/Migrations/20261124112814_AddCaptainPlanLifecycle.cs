using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildMind.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCaptainPlanLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plan_confirmations",
                columns: table => new
                {
                    confirmation_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    project_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    plan_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    confirmed_revision_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    geometry_hash = table.Column<string>(type: "varchar(71)", maxLength: 71, nullable: false),
                    validation_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    confirmed_by = table.Column<Guid>(type: "char(36)", nullable: false),
                    confirmed_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    warning_acknowledgement = table.Column<bool>(type: "tinyint(1)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_confirmations", x => x.confirmation_id);
                    table.ForeignKey(
                        name: "fk_plan_confirmations_plan_revisions",
                        columns: x => new { x.project_id, x.plan_id, x.confirmed_revision_id },
                        principalTable: "plan_revisions",
                        principalColumns: new[] { "project_id", "plan_id", "revision_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_confirmations_users_confirmed_by",
                        column: x => x.confirmed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "plan_edit_candidates",
                columns: table => new
                {
                    candidate_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    project_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    plan_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    owner_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    base_revision_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    land_revision_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    requirements_revision_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    command_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    geometry_json = table.Column<string>(type: "mediumtext", nullable: false),
                    geometry_hash = table.Column<string>(type: "varchar(71)", maxLength: 71, nullable: false),
                    geometry_schema_version = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    validation_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    expires_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_edit_candidates", x => x.candidate_id);
                    table.ForeignKey(
                        name: "fk_plan_edit_candidates_plan_validation_evidence",
                        column: x => x.validation_id,
                        principalTable: "plan_validation_evidence",
                        principalColumn: "validation_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_edit_candidates_plans",
                        columns: x => new { x.project_id, x.plan_id },
                        principalTable: "plans",
                        principalColumns: new[] { "project_id", "plan_id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_plan_edit_candidates_users_owner_id",
                        column: x => x.owner_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_plan_confirmations_confirmed_by",
                table: "plan_confirmations",
                column: "confirmed_by");

            migrationBuilder.CreateIndex(
                name: "ix_plan_confirmations_revision",
                table: "plan_confirmations",
                columns: new[] { "project_id", "plan_id", "confirmed_revision_id" });

            migrationBuilder.CreateIndex(
                name: "ux_plan_confirmations_plan",
                table: "plan_confirmations",
                columns: new[] { "project_id", "plan_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plan_edit_candidates_owner_id",
                table: "plan_edit_candidates",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_plan_edit_candidates_validation_id",
                table: "plan_edit_candidates",
                column: "validation_id");

            migrationBuilder.CreateIndex(
                name: "ux_plan_edit_candidates_plan_command",
                table: "plan_edit_candidates",
                columns: new[] { "project_id", "plan_id", "command_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "plan_confirmations");

            migrationBuilder.DropTable(
                name: "plan_edit_candidates");
        }
    }
}
