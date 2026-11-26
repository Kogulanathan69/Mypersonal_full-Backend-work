using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildMind.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGenerationEvidencePackId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "evidence_pack_id",
                table: "generation_jobs",
                type: "varchar(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "evidence_pack_id",
                table: "generation_jobs");
        }
    }
}
