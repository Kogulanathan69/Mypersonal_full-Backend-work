using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BuildMind.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLandCoordinateFrame : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "frame_json",
                table: "land_revisions",
                type: "mediumtext",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "geometry_digest",
                table: "land_revisions",
                type: "char(64)",
                fixedLength: true,
                maxLength: 64,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "frame_json",
                table: "land_revisions");

            migrationBuilder.DropColumn(
                name: "geometry_digest",
                table: "land_revisions");
        }
    }
}
