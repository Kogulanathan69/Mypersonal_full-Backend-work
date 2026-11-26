using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace BuildMind.Persistence.IntegrationTests.Model;

/// <summary>
/// Hand-written spike migration (test-only, synthetic schema). Column types are left to the provider except
/// timestamps, which use <c>datetime(6)</c> to match the M4 model convention (MySqlModelConventions).
/// </summary>
[DbContext(typeof(SpikeDbContext))]
[Migration(Id)]
public sealed class SpikeInitialMigration : Migration
{
    public const string Id = "20261118000000_SpikeInitial";

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "spike_owners",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                name = table.Column<string>(maxLength: 100, nullable: false),
                revision = table.Column<long>(nullable: false),
                created_at_utc = table.Column<DateTimeOffset>(type: "datetime(6)", nullable: false),
            },
            constraints: table => table.PrimaryKey("pk_spike_owners", x => x.id));

        migrationBuilder.CreateIndex(name: "ux_spike_owners_name", table: "spike_owners", column: "name", unique: true);

        migrationBuilder.CreateTable(
            name: "spike_items",
            columns: table => new
            {
                id = table.Column<Guid>(nullable: false),
                owner_id = table.Column<Guid>(nullable: false),
                amount = table.Column<decimal>(precision: 18, scale: 4, nullable: false),
                payload_json = table.Column<string>(type: "json", nullable: false),
                revision = table.Column<long>(nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_spike_items", x => x.id);
                table.ForeignKey(
                    name: "fk_spike_items_spike_owners_owner_id",
                    column: x => x.owner_id,
                    principalTable: "spike_owners",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(name: "ix_spike_items_owner_id", table: "spike_items", column: "owner_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "spike_items");
        migrationBuilder.DropTable(name: "spike_owners");
    }
}
