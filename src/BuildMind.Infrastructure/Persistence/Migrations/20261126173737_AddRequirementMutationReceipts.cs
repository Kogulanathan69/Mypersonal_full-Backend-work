using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildMind.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRequirementMutationReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "requirement_mutation_receipts",
                columns: table => new
                {
                    project_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    actor_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    operation_id = table.Column<Guid>(type: "char(36)", nullable: false),
                    authorization_version = table.Column<long>(type: "bigint", nullable: false),
                    category = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    format_version = table.Column<byte>(type: "tinyint unsigned", nullable: false),
                    digest = table.Column<byte[]>(type: "binary(32)", nullable: false),
                    saved_revision_id = table.Column<Guid>(type: "char(36)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_requirement_mutation_receipts", x => new { x.project_id, x.actor_id, x.operation_id });
                    table.ForeignKey(
                        name: "fk_requirement_mutation_receipts_revision",
                        columns: x => new { x.project_id, x.saved_revision_id },
                        principalTable: "requirement_revisions",
                        principalColumns: new[] { "project_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                })
                .Annotation("MySQL:Charset", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "ix_requirement_mutation_receipts_revision",
                table: "requirement_mutation_receipts",
                columns: new[] { "project_id", "saved_revision_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "requirement_mutation_receipts");
        }
    }
}
