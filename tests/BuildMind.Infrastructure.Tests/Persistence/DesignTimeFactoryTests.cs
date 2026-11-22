using System.Data;
using BuildMind.Infrastructure.Persistence.DesignTime;
using Microsoft.EntityFrameworkCore;

namespace BuildMind.Infrastructure.Tests.Persistence;

/// <summary>Design-time factory safety (BE-M4-D02a). Nothing here connects to a database.</summary>
public sealed class DesignTimeFactoryTests
{
    private const string Secret = DatabaseOptionsTests.Secret;

    [Fact]
    public void Missing_connection_fails_and_names_only_the_environment_variable()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => BuildMindDesignTimeDbContextFactory.Create(null));

        Assert.Contains(BuildMindDesignTimeDbContextFactory.ConnectionVariable, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Root_account_is_rejected_without_echoing_the_password()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => BuildMindDesignTimeDbContextFactory.Create(
            "Server=127.0.0.1;Port=1;Database=buildmind_synthetic;User ID=root;Password=" + Secret));

        Assert.Contains("root", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Secret, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Valid_connection_builds_a_context_without_connecting()
    {
        using var context = BuildMindDesignTimeDbContextFactory.Create(DatabaseOptionsTests.ValidConnection);

        Assert.StartsWith("MySql", context.Database.ProviderName, StringComparison.Ordinal);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Equal(
     new[]
     {
        PersistenceRegistrationTests.InitialMigrationId,
        PersistenceRegistrationTests.AuthorizationMigrationId,
        PersistenceRegistrationTests.SessionSecurityMigrationId,
        PersistenceRegistrationTests.PlanRevisionHistoryMigrationId
     },
     context.Database.GetMigrations());
    }
}
