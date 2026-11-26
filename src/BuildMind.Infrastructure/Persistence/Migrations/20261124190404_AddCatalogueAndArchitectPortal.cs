using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildMind.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCatalogueAndArchitectPortal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "architect_feedback",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    request_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    author_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    kind = table.Column<string>(type: "varchar(24)", maxLength: 24, nullable: false),
                    body = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_architect_feedback", x => x.id);
                    table.ForeignKey(
                        name: "fk_architect_feedback_requests",
                        column: x => x.request_id,
                        principalTable: "architect_requests",
                        principalColumn: "request_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_architect_feedback_users",
                        column: x => x.author_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "architect_profiles",
                columns: table => new
                {
                    recipient_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    display_name = table.Column<string>(type: "varchar(100)", maxLength: 100, nullable: false),
                    registration_number = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    verification_status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    verification_reference = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: true),
                    verified_by = table.Column<Guid>(type: "char(36)", nullable: true),
                    verified_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    is_listed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    accepting_requests = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_architect_profiles", x => x.recipient_id);
                    table.ForeignKey(
                        name: "fk_architect_profiles_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_architect_profiles_users_verified_by",
                        column: x => x.verified_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "architect_request_responses",
                columns: table => new
                {
                    request_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    recipient_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    decision = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    note = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: true),
                    responded_by = table.Column<Guid>(type: "char(36)", nullable: false),
                    responded_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_architect_request_responses", x => x.request_id);
                    table.ForeignKey(
                        name: "fk_architect_request_responses_requests",
                        column: x => x.request_id,
                        principalTable: "architect_requests",
                        principalColumn: "request_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_architect_request_responses_users",
                        column: x => x.responded_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "notification_preferences",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    email_enabled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_preferences", x => x.user_id);
                    table.ForeignKey(
                        name: "fk_notification_preferences_users",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "notifications",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    kind = table.Column<string>(type: "varchar(48)", maxLength: 48, nullable: false),
                    title = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: false),
                    link = table.Column<string>(type: "varchar(300)", maxLength: 300, nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    read_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notifications", x => x.id);
                    table.ForeignKey(
                        name: "fk_notifications_users_user_id",
                        column: x => x.user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "outbox_messages",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "char(36)", nullable: false),
                    kind = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    recipient_user_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    subject = table.Column<string>(type: "varchar(200)", maxLength: 200, nullable: false),
                    body = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    attempts = table.Column<int>(type: "int", nullable: false),
                    next_attempt_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    sent_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    last_error_code = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: true),
                    dedupe_key = table.Column<string>(type: "varchar(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_outbox_messages", x => x.id);
                    table.ForeignKey(
                        name: "fk_outbox_messages_users_recipient",
                        column: x => x.recipient_user_id,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "requirement_catalogue_versions",
                columns: table => new
                {
                    version = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false),
                    provenance = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false),
                    document_json = table.Column<string>(type: "mediumtext", nullable: false),
                    effective_from_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    row_version = table.Column<long>(type: "bigint", nullable: false),
                    created_by = table.Column<Guid>(type: "char(36)", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    published_by = table.Column<Guid>(type: "char(36)", nullable: true),
                    published_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    retired_at_utc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requirement_catalogue_versions", x => x.version);
                    table.ForeignKey(
                        name: "fk_requirement_catalogue_versions_created_by",
                        column: x => x.created_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_requirement_catalogue_versions_published_by",
                        column: x => x.published_by,
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_architect_feedback_author_id",
                table: "architect_feedback",
                column: "author_id");

            migrationBuilder.CreateIndex(
                name: "ix_architect_feedback_request_id",
                table: "architect_feedback",
                columns: new[] { "request_id", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_architect_profiles_verified_by",
                table: "architect_profiles",
                column: "verified_by");

            migrationBuilder.CreateIndex(
                name: "ux_architect_profiles_user_id",
                table: "architect_profiles",
                column: "user_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_architect_request_responses_responded_by",
                table: "architect_request_responses",
                column: "responded_by");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_user_unread",
                table: "notifications",
                columns: new[] { "user_id", "read_at_utc", "created_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_due",
                table: "outbox_messages",
                columns: new[] { "status", "next_attempt_at_utc" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_messages_recipient",
                table: "outbox_messages",
                column: "recipient_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_outbox_messages_dedupe_key",
                table: "outbox_messages",
                column: "dedupe_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_requirement_catalogue_versions_created_by",
                table: "requirement_catalogue_versions",
                column: "created_by");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_catalogue_versions_published_by",
                table: "requirement_catalogue_versions",
                column: "published_by");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_catalogue_versions_served",
                table: "requirement_catalogue_versions",
                columns: new[] { "status", "effective_from_utc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "architect_feedback");

            migrationBuilder.DropTable(
                name: "architect_profiles");

            migrationBuilder.DropTable(
                name: "architect_request_responses");

            migrationBuilder.DropTable(
                name: "notification_preferences");

            migrationBuilder.DropTable(
                name: "notifications");

            migrationBuilder.DropTable(
                name: "outbox_messages");

            migrationBuilder.DropTable(
                name: "requirement_catalogue_versions");
        }
    }
}
