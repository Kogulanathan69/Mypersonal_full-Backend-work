using BuildMind.Infrastructure.Persistence.Repositories.Requirements;

namespace BuildMind.Infrastructure.Tests.Persistence;

public sealed class RequirementMutationFingerprintTests
{
    [Fact]
    public void Draft_digest_is_stable_and_binds_each_scope_field()
    {
        var project = Guid.Parse("00000000-0000-4000-8000-000000000001");
        var actor = Guid.Parse("00000000-0000-4000-8000-000000000002");
        var operation = Guid.Parse("00000000-0000-4000-8000-000000000003");
        var revision = Guid.Parse("00000000-0000-4000-8000-000000000004");
        var original = RequirementMutationFingerprint.Compute(project, actor, operation, 1, revision, "{}"u8);
        Assert.Equal(32, original.Length);
        Assert.Equal(original, RequirementMutationFingerprint.Compute(project, actor, operation, 1, revision, "{}"u8));
        foreach (var changed in new[]
                 {
                     RequirementMutationFingerprint.Compute(actor, actor, operation, 1, revision, "{}"u8),
                     RequirementMutationFingerprint.Compute(project, project, operation, 1, revision, "{}"u8),
                     RequirementMutationFingerprint.Compute(project, actor, actor, 1, revision, "{}"u8),
                     RequirementMutationFingerprint.Compute(project, actor, operation, 2, revision, "{}"u8),
                     RequirementMutationFingerprint.Compute(project, actor, operation, 1, null, "{}"u8),
                     RequirementMutationFingerprint.Compute(project, actor, operation, 1, revision, "[]"u8)
                 })
            Assert.False(original.AsSpan().SequenceEqual(changed));

        Assert.Throws<ArgumentException>(() => RequirementMutationFingerprint.Compute(project, actor, operation, 0, revision, "{}"u8));
        Assert.Throws<ArgumentException>(() => RequirementMutationFingerprint.Compute(project, actor, operation, 1, Guid.Empty, "{}"u8));
    }
}
