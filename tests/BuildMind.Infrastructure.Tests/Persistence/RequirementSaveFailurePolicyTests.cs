using BuildMind.Infrastructure.Persistence.Errors;
using BuildMind.Infrastructure.Persistence.Repositories.Requirements;

namespace BuildMind.Infrastructure.Tests.Persistence;

/// <summary>
/// How a database failure during a requirement save is reported (STEP 06-C audit). A failure becomes a domain
/// conflict only when the head verifiably moved; unrelated contention and other constraint failures are rethrown.
/// Messages use MySQL's real formats: duplicate keys are reported as 'table.key' and every primary key is named
/// PRIMARY.
/// </summary>
public sealed class RequirementSaveFailurePolicyTests
{
    private static PersistenceFailure Duplicate(string tableDotKey) =>
        PersistenceErrorClassifier.FromProviderError(1062, $"Duplicate entry 'SYNTHETIC' for key '{tableDotKey}'");

    private static PersistenceFailure ForeignKey(string constraint) =>
        PersistenceErrorClassifier.FromProviderError(
            1452,
            $"Cannot add or update a child row: a foreign key constraint fails (`db`.`t`, CONSTRAINT `{constraint}` FOREIGN KEY (`c`) REFERENCES `p` (`id`))");

    [Theory]
    [InlineData("requirement_revisions.ux_requirement_revisions_project_id_revision_number")]
    [InlineData("project_requirement_heads.PRIMARY")]
    [InlineData("requirement_revisions.PRIMARY")]
    public void Duplicate_is_a_conflict_only_when_the_head_moved(string key)
    {
        Assert.Equal(SaveFailureResolution.RevisionConflict,
            RequirementRevisionRepository.ResolveSaveFailure(Duplicate(key), headMoved: true));
    }

    [Fact]
    public void Deadlock_is_a_conflict_when_the_head_moved()
    {
        Assert.Equal(SaveFailureResolution.RevisionConflict,
            RequirementRevisionRepository.ResolveSaveFailure(PersistenceErrorClassifier.FromProviderError(1213, null), headMoved: true));
    }

    [Fact]
    public void Deadlock_with_an_unchanged_head_is_unrelated_contention_and_rethrown()
    {
        Assert.Equal(SaveFailureResolution.Rethrow,
            RequirementRevisionRepository.ResolveSaveFailure(PersistenceErrorClassifier.FromProviderError(1213, null), headMoved: false));
    }

    [Fact]
    public void Reusing_an_existing_revision_identity_is_a_caller_error_not_a_conflict()
    {
        var failure = Duplicate("requirement_revisions.PRIMARY");

        Assert.Equal("PRIMARY", failure.ConstraintName);
        Assert.Equal("requirement_revisions", failure.ConstraintTable);
        Assert.Equal(SaveFailureResolution.RevisionIdentityReused,
            RequirementRevisionRepository.ResolveSaveFailure(failure, headMoved: false));
    }

    [Theory]
    [InlineData("requirement_revisions.ux_requirement_revisions_project_id_revision_number")]
    [InlineData("project_requirement_heads.PRIMARY")]      // another table's primary key is NOT identity reuse
    [InlineData("users.ux_users_normalized_email")]
    [InlineData("PRIMARY")]                                 // no table reported: cannot be attributed
    public void Other_duplicates_with_an_unchanged_head_are_rethrown(string key)
    {
        Assert.Equal(SaveFailureResolution.Rethrow,
            RequirementRevisionRepository.ResolveSaveFailure(Duplicate(key), headMoved: false));
    }

    [Theory]
    [InlineData("fk_requirement_revisions_projects_project_id")]
    [InlineData("fk_project_requirement_heads_projects_project_id")]
    public void Project_foreign_key_violations_mean_the_project_is_missing(string constraint)
    {
        Assert.Equal(SaveFailureResolution.ProjectNotFound,
            RequirementRevisionRepository.ResolveSaveFailure(ForeignKey(constraint), headMoved: false));
    }

    [Theory]
    [InlineData("fk_project_requirement_heads_requirement_revisions_current")]
    [InlineData("fk_unrelated")]
    public void Other_foreign_key_violations_are_rethrown(string constraint)
    {
        Assert.Equal(SaveFailureResolution.Rethrow,
            RequirementRevisionRepository.ResolveSaveFailure(ForeignKey(constraint), headMoved: false));
    }

    [Theory]
    [InlineData(1205)] // lock wait timeout: not a conflict, not fully rolled back by the server
    [InlineData(1042)] // connection failure
    [InlineData(1064)] // unexpected
    public void Other_failures_are_rethrown_even_if_the_head_moved(int number)
    {
        Assert.Equal(SaveFailureResolution.Rethrow,
            RequirementRevisionRepository.ResolveSaveFailure(PersistenceErrorClassifier.FromProviderError(number, null), headMoved: true));
    }

    [Fact]
    public void Constraint_table_is_extracted_without_any_value()
    {
        var failure = Duplicate("users.ux_users_normalized_email");

        Assert.Equal("users", failure.ConstraintTable);
        Assert.DoesNotContain("SYNTHETIC", failure.ToString(), StringComparison.Ordinal);
    }
}
