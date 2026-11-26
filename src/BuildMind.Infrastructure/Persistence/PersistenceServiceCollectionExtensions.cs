using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Infrastructure.Configuration;
using BuildMind.Infrastructure.Persistence.Repositories.Requirements;
using BuildMind.Infrastructure.Persistence.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace BuildMind.Infrastructure.Persistence;

/// <summary>Registers BuildMind persistence services (BE-M4-D02a).</summary>
internal static class PersistenceServiceCollectionExtensions
{
    /// <summary>Safe, fixed message used when database services are requested while the database is disabled.</summary>
    public const string DisabledMessage =
        "Database access is disabled (" + DatabaseOptions.SectionName + ":Enabled is false).";

    public static IServiceCollection AddBuildMindPersistence(this IServiceCollection services)
    {
        // Options are always resolved lazily: nothing here reads configuration, opens a connection,
        // creates a database or applies migrations. Binding and start-up validation of DatabaseOptions
        // happen in the API composition root (decision C8).
        services.AddDbContext<BuildMindDbContext>(ConfigureContext);

        services.TryAddScoped<IDbTransactionSource, DbContextTransactionSource>();
        services.TryAddScoped<TransactionRunner>();

        // BE-M4-D03: persistence adapters for existing Application ports (only where the port's semantics and a
        // reflection-free domain reconstruction exist). Resolving them while the database is disabled fails safely.
        services.TryAddScoped<IRequirementRevisionRepository, RequirementRevisionRepository>();

        return services;
    }

    private static void ConfigureContext(IServiceProvider provider, DbContextOptionsBuilder builder)
    {
        var options = provider.GetRequiredService<IOptions<DatabaseOptions>>().Value;
        if (!options.Enabled)
        {
            throw new InvalidOperationException(DisabledMessage);
        }

        var connectionString = provider.GetRequiredService<IConfiguration>()
            .GetConnectionString(DatabaseOptions.ConnectionStringName);

        // Re-checked here so a context can never be built from an unvalidated connection string,
        // even if start-up validation was bypassed.
        if (DatabaseOptions.ValidateConnectionString(connectionString, DatabaseOptions.ConnectionStringKey).Count > 0)
        {
            throw new InvalidOperationException(
                $"{DatabaseOptions.ConnectionStringKey} is missing or invalid; see start-up validation.");
        }

        MySqlContextOptions.Apply(builder, connectionString!, options.CommandTimeoutSeconds);
    }
}
