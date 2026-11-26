using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildMind.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddPlanRevisionHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "plan_revisions",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    plan_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    revision_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    parent_revision_id = table.Column<Guid>(type: "char(36)", nullable: true),
                    version_number = table.Column<long>(type: "bigint", nullable: false),
                    geometry_schema_version = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    geometry_json = table.Column<string>(type: "mediumtext", nullable: false),
                    geometry_hash = table.Column<byte[]>(type: "varbinary(32)", nullable: false),
                    land_revision_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    requirements_revision_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    edit_command_id = table.Column<Guid>(type: "char(36)", nullable: true),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    change_summary = table.Column<string>(type: "varchar(1024)", maxLength: 1024, nullable: true),
                    validation_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    validator_version = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    validation_geometry_hash = table.Column<byte[]>(type: "varbinary(32)", nullable: false),
                    geometry_valid = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    requirements_match = table.Column<bool>(type: "tinyint(1)", nullable: true),
                    regulatory_evidence_status = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    has_blocking_issues = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    validated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plan_revisions", x => new { x.project_id, x.plan_id, x.revision_id });
                    table.CheckConstraint("ck_plan_revisions_digest_sizes", "OCTET_LENGTH(geometry_hash) = 32 AND OCTET_LENGTH(validation_geometry_hash) = 32");
                    table.CheckConstraint("ck_plan_revisions_geometry_size", "OCTET_LENGTH(geometry_json) BETWEEN 1 AND 262144");
                    table.CheckConstraint("ck_plan_revisions_parent", "(version_number = 1 AND parent_revision_id IS NULL) OR (version_number > 1 AND parent_revision_id IS NOT NULL AND parent_revision_id <> revision_id)");
                    table.CheckConstraint("ck_plan_revisions_version", "version_number BETWEEN 1 AND 9223372036854775806");
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "plans",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    plan_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    head_revision_id = table.Column<Guid>(type: "char(36)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_plans", x => new { x.project_id, x.plan_id });
                    table.ForeignKey(
                        name: "fk_plans_projects",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_plan_revisions_parent",
                table: "plan_revisions",
                columns: new[] { "project_id", "plan_id", "parent_revision_id" });

            migrationBuilder.CreateIndex(
                name: "ux_plan_revisions_version",
                table: "plan_revisions",
                columns: new[] { "project_id", "plan_id", "version_number" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_plans_head_revision",
                table: "plans",
                columns: new[] { "project_id", "plan_id", "head_revision_id" });

            // Establish the named supporting indexes before InnoDB can create implicit ones.
            migrationBuilder.AddForeignKey(
                name: "fk_plan_revisions_parent",
                table: "plan_revisions",
                columns: new[] { "project_id", "plan_id", "parent_revision_id" },
                principalTable: "plan_revisions",
                principalColumns: new[] { "project_id", "plan_id", "revision_id" });

            migrationBuilder.AddForeignKey(
                name: "fk_plans_plan_revisions_head",
                table: "plans",
                columns: new[] { "project_id", "plan_id", "head_revision_id" },
                principalTable: "plan_revisions",
                principalColumns: new[] { "project_id", "plan_id", "revision_id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "fk_plan_revisions_plans",
                table: "plan_revisions",
                columns: new[] { "project_id", "plan_id" },
                principalTable: "plans",
                principalColumns: new[] { "project_id", "plan_id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "fk_plan_revisions_plans",
                table: "plan_revisions");

            migrationBuilder.DropTable(
                name: "plans");

            migrationBuilder.DropTable(
                name: "plan_revisions");
        }
    }
}
