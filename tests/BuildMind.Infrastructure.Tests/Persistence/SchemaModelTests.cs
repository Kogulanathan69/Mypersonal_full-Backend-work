using BuildMind.Infrastructure.Persistence;
using BuildMind.Infrastructure.Persistence.DesignTime;
using BuildMind.Infrastructure.Persistence.Records;
using BuildMind.Infrastructure.Persistence.Records.PlanEditor;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace BuildMind.Infrastructure.Tests.Persistence;

/// <summary>
/// The BE-M4-D03 relational model, verified from EF metadata (no database). Live schema checks run in the
/// MySQL compatibility CI job.
/// </summary>
public sealed class SchemaModelTests
{
    private static IModel Model()
    {
        using var context = BuildMindDesignTimeDbContextFactory.Create(DatabaseOptionsTests.ValidConnection);
        return context.GetService<Microsoft.EntityFrameworkCore.Metadata.IDesignTimeModel>().Model;
    }

    private static IEntityType Entity<T>() => Model().FindEntityType(typeof(T))!;

    [Fact]
    public void Model_contains_exactly_the_approved_tables()
    {
        var tables = Model().GetEntityTypes().Select(e => e.GetTableName()).OrderBy(t => t, StringComparer.Ordinal);

        Assert.Equal(PersistenceRegistrationTests.ApprovedTables, tables);
    }

    [Fact]
    public void Model_maps_only_infrastructure_records_never_domain_types()
    {
        // Records live under Persistence.Records (feature sub-namespaces allowed); domain types never appear.
        var root = typeof(ProjectRecord).Namespace!;
        Assert.Contains(typeof(PlanRecord).Namespace, new[] { root + ".PlanEditor" });
        Assert.All(Model().GetEntityTypes(), e =>
        {
            Assert.True(e.ClrType.Namespace == root || e.ClrType.Namespace!.StartsWith(root + ".", StringComparison.Ordinal),
                e.ClrType.FullName);
            Assert.False(e.ClrType.Namespace!.StartsWith("BuildMind.Domain", StringComparison.Ordinal));
        });
    }

    [Fact]
    public void Every_column_name_is_lowercase_snake_case()
    {
        var columns = Model().GetEntityTypes()
            .SelectMany(e => e.GetProperties().Select(p => p.GetColumnName()));

        Assert.All(columns, c => Assert.Matches("^[a-z][a-z0-9_]*$", c));
    }

    [Fact]
    public void Every_key_index_and_constraint_name_is_lowercase_snake_case()
    {
        var entities = Model().GetEntityTypes().ToArray();
        var names = entities.SelectMany(e => e.GetKeys().Select(k => k.GetName()))
            .Concat(entities.SelectMany(e => e.GetIndexes().Select(i => i.GetDatabaseName())))
            .Concat(entities.SelectMany(e => e.GetForeignKeys().Select(fk => fk.GetConstraintName())))
            .ToArray();

        Assert.NotEmpty(names);
        Assert.All(names, n => Assert.Matches("^(pk|ak|fk|ux|ix)_[a-z0-9_]+$", n));
    }

    [Fact]
    public void Primary_keys_are_explicit_and_never_database_generated()
    {
        Assert.All(Model().GetEntityTypes(), e =>
        {
            var key = e.FindPrimaryKey()!;
            Assert.StartsWith("pk_", key.GetName(), StringComparison.Ordinal);
            Assert.All(key.Properties, p => Assert.Equal(ValueGenerated.Never, p.ValueGenerated));
        });
    }

    [Fact]
    public void Foreign_keys_are_explicit_and_restrict_deletes()
    {
        var foreignKeys = Model().GetEntityTypes().SelectMany(e => e.GetForeignKeys()).ToArray();

        Assert.Equal(
            new[]
            {
                "fk_password_reset_tokens_users_user_id",
                "fk_plan_revisions_parent",
                "fk_plan_revisions_plans",
                "fk_plans_plan_revisions_head",
                "fk_plans_projects",
                "fk_project_requirement_heads_projects_project_id",
                "fk_project_requirement_heads_requirement_revisions_current",
                "fk_projects_users_owner_id",
                "fk_refresh_tokens_users_user_id",
                "fk_requirement_revisions_projects_project_id",
            }.Concat(PersistenceRegistrationTests.CaptainMigrations.SelectMany(m => m.ForeignKeys)).Order(StringComparer.Ordinal),
            foreignKeys.Select(fk => fk.GetConstraintName()).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(foreignKeys, fk => Assert.Equal(
            fk.GetConstraintName() == "fk_plan_revisions_parent" ? DeleteBehavior.ClientNoAction : DeleteBehavior.Restrict,
            fk.DeleteBehavior));
    }

    [Fact]
    public void Receipt_restricts_deletion_of_the_same_project_revision()
    {
        var fk = Entity<BuildMind.Infrastructure.Persistence.Records.Requirements.RequirementMutationReceiptRecord>()
            .GetForeignKeys().Single();
        Assert.Equal(typeof(RequirementRevisionRecord), fk.PrincipalEntityType.ClrType);
        Assert.Equal(new[] { "project_id", "saved_revision_id" }, fk.Properties.Select(p => p.GetColumnName()));
        Assert.Equal("ak_requirement_revisions_project_id_id", fk.PrincipalKey.GetName());
        Assert.Equal(new[] { "project_id", "id" }, fk.PrincipalKey.Properties.Select(p => p.GetColumnName()));
        Assert.Equal(DeleteBehavior.Restrict, fk.DeleteBehavior);
    }

    [Fact]
    public void Head_pointer_can_only_reference_a_revision_of_the_same_project()
    {
        var fk = Entity<RequirementHeadRecord>().GetForeignKeys()
            .Single(f => f.PrincipalEntityType.ClrType == typeof(RequirementRevisionRecord));

        Assert.Equal(new[] { "project_id", "current_revision_id" }, fk.Properties.Select(p => p.GetColumnName()));
        Assert.Equal(new[] { "project_id", "id" }, fk.PrincipalKey.Properties.Select(p => p.GetColumnName()));
    }

    [Fact]
    public void Unique_constraints_protect_identity_and_numbering()
    {
        var userEmail = Entity<UserRecord>().GetIndexes().Single(i => i.GetDatabaseName() == "ux_users_normalized_email");
        var numbering = Entity<RequirementRevisionRecord>().GetIndexes()
            .Single(i => i.GetDatabaseName() == "ux_requirement_revisions_project_id_revision_number");

        Assert.True(userEmail.IsUnique);
        Assert.True(numbering.IsUnique);
        Assert.Equal(new[] { "project_id", "revision_number" }, numbering.Properties.Select(p => p.GetColumnName()));
    }

    [Fact]
    public void Owner_scoped_project_queries_have_a_leading_owner_index()
    {
        var index = Entity<ProjectRecord>().GetIndexes().Single(i => i.GetDatabaseName() == "ix_projects_owner_id_created_at_utc");

        Assert.Equal(new[] { "owner_id", "created_at_utc" }, index.Properties.Select(p => p.GetColumnName()));
    }

    [Fact]
    public void Project_revision_is_a_concurrency_token()
    {
        Assert.True(Entity<ProjectRecord>().FindProperty(nameof(ProjectRecord.Revision))!.IsConcurrencyToken);
    }

    [Theory]
    [InlineData(typeof(UserRecord), nameof(UserRecord.CreatedAtUtc))]
    [InlineData(typeof(ProjectRecord), nameof(ProjectRecord.CreatedAtUtc))]
    [InlineData(typeof(ProjectRecord), nameof(ProjectRecord.UpdatedAtUtc))]
    [InlineData(typeof(RequirementRevisionRecord), nameof(RequirementRevisionRecord.CreatedAtUtc))]
    public void Timestamps_are_utc_datetime6_with_converter(Type entity, string property)
    {
        var mapped = Model().FindEntityType(entity)!.FindProperty(property)!;

        Assert.Equal("datetime(6)", mapped.GetColumnType());
        Assert.NotNull(mapped.GetValueConverter());
    }

    [Fact]
    public void Requirement_content_is_native_json_and_required()
    {
        var content = Entity<RequirementRevisionRecord>().FindProperty(nameof(RequirementRevisionRecord.ContentJson))!;

        Assert.Equal("json", content.GetColumnType());
        Assert.False(content.IsNullable);
    }

    [Theory]
    [InlineData(typeof(UserRecord), nameof(UserRecord.Email), 254)]
    [InlineData(typeof(UserRecord), nameof(UserRecord.NormalizedEmail), 254)]
    [InlineData(typeof(UserRecord), nameof(UserRecord.FullName), 100)]
    [InlineData(typeof(UserRecord), nameof(UserRecord.PhoneNumber), 16)]
    [InlineData(typeof(ProjectRecord), nameof(ProjectRecord.Name), 150)]
    [InlineData(typeof(ProjectRecord), nameof(ProjectRecord.ProjectType), 100)]
    [InlineData(typeof(ProjectRecord), nameof(ProjectRecord.LocationReference), 500)]
    [InlineData(typeof(RequirementRevisionRecord), nameof(RequirementRevisionRecord.SchemaVersion), 64)]
    public void Column_lengths_follow_the_feature_validators(Type entity, string property, int length)
    {
        Assert.Equal(length, Model().FindEntityType(entity)!.FindProperty(property)!.GetMaxLength());
    }

    [Theory]
    [InlineData(typeof(UserRecord), nameof(UserRecord.FullName), true)]
    [InlineData(typeof(UserRecord), nameof(UserRecord.PasswordHash), false)]
    [InlineData(typeof(ProjectRecord), nameof(ProjectRecord.ProjectType), true)]
    [InlineData(typeof(ProjectRecord), nameof(ProjectRecord.Name), false)]
    [InlineData(typeof(ProjectRecord), nameof(ProjectRecord.UpdatedAtUtc), true)]
    public void Required_and_optional_fields_match_the_domain(Type entity, string property, bool nullable)
    {
        Assert.Equal(nullable, Model().FindEntityType(entity)!.FindProperty(property)!.IsNullable);
    }
}
