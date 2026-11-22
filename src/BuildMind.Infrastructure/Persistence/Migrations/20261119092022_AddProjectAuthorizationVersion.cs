using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildMind.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectAuthorizationVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "authorization_version",
                table: "projects",
                type: "bigint",
                nullable: false,
                defaultValue: 1L);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "authorization_version",
                table: "projects");
        }
    }
}
