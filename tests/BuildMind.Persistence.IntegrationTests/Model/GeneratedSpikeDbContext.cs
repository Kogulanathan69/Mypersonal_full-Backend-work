using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using BuildMind.Infrastructure.Persistence.Connections;

namespace BuildMind.Persistence.IntegrationTests.Model;

/// <summary>
/// Same synthetic model as <see cref="SpikeDbContext"/>, but its migration is produced by the real
/// <c>dotnet ef migrations add</c> command in CI (into <c>GeneratedMigrations/</c>, never committed).
/// It uses its own history table and keeps EF's pending-model-change check ON, so it proves the
/// CLI-generated migration + snapshot path that the hand-written migration cannot.
/// </summary>
public sealed class GeneratedSpikeDbContext(DbContextOptions<GeneratedSpikeDbContext> options) : DbContext(options)
{
    public const string HistoryTable = "__generated_spike_migrations";

    public DbSet<SpikeOwner> Owners => Set<SpikeOwner>();

    public DbSet<SpikeItem> Items => Set<SpikeItem>();

    public static GeneratedSpikeDbContext Create(string connectionString) =>
        new(new DbContextOptionsBuilder<GeneratedSpikeDbContext>()
            .UseMySQL(connectionString, mysql =>
            {
                mysql.MigrationsAssembly(typeof(GeneratedSpikeDbContext).Assembly.GetName().Name);
                mysql.MigrationsHistoryTable(HistoryTable);
            })
            .AddInterceptors(MySqlConnectionOpenGate.Interceptor) // STEP 10-E: MySql.Data TLS-state race
            .EnableSensitiveDataLogging(false)
            .Options);

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        BuildMind.Infrastructure.Persistence.MySqlModelConventions.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder) => SpikeModel.Configure(modelBuilder);
}

/// <summary>
/// Design-time factory for <c>dotnet ef migrations add/script</c>. Uses a fixed, credential-free placeholder
/// connection string on an unreachable port: generating migrations and scripts never connects.
/// </summary>
public sealed class GeneratedSpikeDesignTimeFactory : IDesignTimeDbContextFactory<GeneratedSpikeDbContext>
{
    public GeneratedSpikeDbContext CreateDbContext(string[] args) =>
        GeneratedSpikeDbContext.Create("Server=127.0.0.1;Port=1;Database=buildmind_test_design;User ID=bm_test;SslMode=Required");
}
