using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.Persistence;

/// <summary>
/// The single BuildMind EF Core context (M4 is the single writer for this type and for migrations).
/// </summary>
/// <remarks>
/// BE-M4-D03 maps Infrastructure persistence records (<c>Persistence.Records</c>), never domain types directly:
/// users, projects, requirement revisions and the per-project requirement head. Feature mappings are added only
/// with the owning member's agreement, as <see cref="IEntityTypeConfiguration{TEntity}"/> classes under
/// <c>BuildMind.Infrastructure.Persistence.Configurations.&lt;Feature&gt;</c>, which are discovered here.
/// The context never creates or migrates the database itself.
/// </remarks>
public sealed class BuildMindDbContext(DbContextOptions<BuildMindDbContext> options) : DbContext(options)
{
    /// <summary>Namespace prefix from which entity configurations are discovered.</summary>
    public const string ConfigurationsNamespace = "BuildMind.Infrastructure.Persistence.Configurations";

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder) =>
        MySqlModelConventions.Apply(configurationBuilder);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(BuildMindDbContext).Assembly,
            type => type.Namespace is not null
                && (type.Namespace == ConfigurationsNamespace
                    || type.Namespace.StartsWith(ConfigurationsNamespace + ".", StringComparison.Ordinal)));
    }
}
