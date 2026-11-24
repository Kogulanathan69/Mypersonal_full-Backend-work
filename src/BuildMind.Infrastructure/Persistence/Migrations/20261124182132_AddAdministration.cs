using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildMind.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAdministration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "admin_audit_chain_head",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false),
                    last_sequence = table.Column<long>(type: "bigint", nullable: false),
                    last_hash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_audit_chain_head", x => x.id);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "admin_audit_events",
                columns: table => new
                {
                    sequence = table.Column<long>(type: "bigint", nullable: false),
                    actor_id = table.Column<Guid>(type: "char(36)", nullable: true),
                    action = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    target_type = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    target_id = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: true),
                    purpose = table.Column<string>(type: "varchar(500)", maxLength: 500, nullable: true),
                    details_json = table.Column<string>(type: "text", nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    previous_hash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false),
                    hash = table.Column<string>(type: "char(64)", fixedLength: true, maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_admin_audit_events", x => x.sequence);
                    table.ForeignKey(
                        name: "fk_admin_audit_events_users_actor_id",
                        column: x => x.actor_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "principal_roles",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    role = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    is_active = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    granted_by = table.Column<Guid>(type: "char(36)", nullable: true),
                    granted_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    revoked_by = table.Column<Guid>(type: "char(36)", nullable: true),
                    revoked_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_principal_roles", x => x.id);
                    table.ForeignKey(
                        name: "fk_principal_roles_users_granted_by",
                        column: x => x.granted_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_principal_roles_users_revoked_by",
                        column: x => x.revoked_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_principal_roles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "staff_change_proposals",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    module = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    target_type = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    target_key = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false),
                    change_type = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    base_version = table.Column<long>(type: "bigint", nullable: true),
                    payload_json = table.Column<string>(type: "mediumtext", nullable: false),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    proposed_by = table.Column<Guid>(type: "char(36)", nullable: false),
                    proposed_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    reviewed_by = table.Column<Guid>(type: "char(36)", nullable: true),
                    reviewed_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    review_note = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_change_proposals", x => x.id);
                    table.ForeignKey(
                        name: "fk_staff_change_proposals_users_proposed_by",
                        column: x => x.proposed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_staff_change_proposals_users_reviewed_by",
                        column: x => x.reviewed_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "staff_profiles",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    modules_csv = table.Column<string>(type: "varchar(512)", maxLength: 512, nullable: false),
                    scope_version = table.Column<long>(type: "bigint", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_profiles", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_staff_profiles_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_staff_profiles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "staff_tasks",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    description = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    module = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false),
                    project_id = table.Column<Guid>(type: "char(36)", nullable: true),
                    assigned_to = table.Column<Guid>(type: "char(36)", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: false),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_tasks", x => x.id);
                    table.ForeignKey(
                        name: "fk_staff_tasks_projects_project_id",
                        column: x => x.project_id,
                        principalTable: "projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_staff_tasks_users_assigned_to",
                        column: x => x.assigned_to,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_staff_tasks_users_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "system_policies",
                columns: table => new
                {
                    policy_key = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    value_json = table.Column<string>(type: "text", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    updated_by = table.Column<Guid>(type: "char(36)", nullable: true),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_system_policies", x => x.policy_key);
                    table.ForeignKey(
                        name: "fk_system_policies_users_updated_by",
                        column: x => x.updated_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "staff_task_notes",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    task_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    author_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    kind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    body = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_staff_task_notes", x => x.id);
                    table.ForeignKey(
                        name: "fk_staff_task_notes_staff_tasks_task_id",
                        column: x => x.task_id,
                        principalTable: "staff_tasks",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_staff_task_notes_users_author_id",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.InsertData(
                table: "admin_audit_chain_head",
                columns: new[] { "id", "last_hash", "last_sequence" },
                values: new object[] { 1, "0000000000000000000000000000000000000000000000000000000000000000", 0L });

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_events_actor_id",
                table: "admin_audit_events",
                column: "actor_id");

            migrationBuilder.CreateIndex(
                name: "ix_admin_audit_events_target",
                table: "admin_audit_events",
                columns: new[] { "target_type", "target_id" });

            migrationBuilder.CreateIndex(
                name: "ix_principal_roles_granted_by",
                table: "principal_roles",
                column: "granted_by");

            migrationBuilder.CreateIndex(
                name: "ix_principal_roles_revoked_by",
                table: "principal_roles",
                column: "revoked_by");

            migrationBuilder.CreateIndex(
                name: "ix_principal_roles_role_is_active",
                table: "principal_roles",
                columns: new[] { "role", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ux_principal_roles_user_id_role",
                table: "principal_roles",
                columns: new[] { "user_id", "role" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_staff_change_proposals_proposed_by",
                table: "staff_change_proposals",
                column: "proposed_by");

            migrationBuilder.CreateIndex(
                name: "ix_staff_change_proposals_reviewed_by",
                table: "staff_change_proposals",
                column: "reviewed_by");

            migrationBuilder.CreateIndex(
                name: "ix_staff_change_proposals_status",
                table: "staff_change_proposals",
                columns: new[] { "status", "proposed_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_profiles_created_by",
                table: "staff_profiles",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_staff_task_notes_author_id",
                table: "staff_task_notes",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_staff_task_notes_task_id_created",
                table: "staff_task_notes",
                columns: new[] { "task_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_tasks_assigned_to_status",
                table: "staff_tasks",
                columns: new[] { "assigned_to", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_staff_tasks_created_by",
                table: "staff_tasks",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_staff_tasks_project_id",
                table: "staff_tasks",
                column: "project_id");

            migrationBuilder.CreateIndex(
                name: "ix_system_policies_updated_by",
                table: "system_policies",
                column: "updated_by");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "admin_audit_chain_head");

            migrationBuilder.DropTable(
                name: "admin_audit_events");

            migrationBuilder.DropTable(
                name: "principal_roles");

            migrationBuilder.DropTable(
                name: "staff_change_proposals");

            migrationBuilder.DropTable(
                name: "staff_profiles");

            migrationBuilder.DropTable(
                name: "staff_task_notes");

            migrationBuilder.DropTable(
                name: "system_policies");

            migrationBuilder.DropTable(
                name: "staff_tasks");
        }
    }
}
