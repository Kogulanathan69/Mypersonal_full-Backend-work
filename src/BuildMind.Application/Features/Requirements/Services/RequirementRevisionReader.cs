using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Domain.Entities;

namespace BuildMind.Application.Features.Requirements.Services;

/// <summary>Reads immutable snapshots through the existing repository and verifies response identities.</summary>
/// <remarks>
/// Not an authorization guard. A future trusted caller must establish the agreed project-access and
/// ownership-consistency guarantee. Null means only that the repository found no matching revision;
/// it does not establish project existence, HTTP status, policy validity or generation readiness.
/// Deliberately unregistered in production DI. Dependency failures propagate without retries.
/// </remarks>
public sealed class RequirementRevisionReader
{
    private readonly IRequirementRevisionRepository _repository;

    public RequirementRevisionReader(IRequirementRevisionRepository repository) =>
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));

    public async Task<RequirementRevision?> GetCurrentAsync(Guid projectId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireIdentity(projectId, nameof(projectId));
        var revision = await _repository.GetCurrentAsync(projectId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (revision is not null && revision.ProjectId != projectId)
            throw new InvalidOperationException("The repository returned an inconsistent project identity.");
        return revision;
    }

    public async Task<RequirementRevision?> GetAsync(Guid projectId, Guid revisionId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        RequireIdentity(projectId, nameof(projectId));
        RequireIdentity(revisionId, nameof(revisionId));
        var revision = await _repository.GetAsync(projectId, revisionId, cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (revision is not null && (revision.ProjectId != projectId || revision.Id != revisionId))
            throw new InvalidOperationException("The repository returned an inconsistent revision identity.");
        return revision;
    }

    private static void RequireIdentity(Guid value, string parameterName)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("A nonempty identity is required.", parameterName);
    }
}
