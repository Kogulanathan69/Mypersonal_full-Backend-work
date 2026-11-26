namespace BuildMind.Application.Features.Requirements.Contracts;

public enum RequirementsOperationStatus
{
    Success, NoSavedRevision, Unauthenticated, NotFound, RevisionConflict, CatalogueConflict,
    ValidationFailed, DependencyUnavailable, DependencyTimeout, InvalidDependencyResponse
}

/// <summary>Guarded, immutable isolated-development outcomes; HTTP behavior belongs to the API module.</summary>
public sealed class RequirementsOperationResult
{
    public RequirementsOperationStatus Status { get; }
    public RequirementRevisionDto? Revision { get; }
    public IReadOnlyList<RequirementValidationIssue> Issues { get; }

    public RequirementsOperationResult(RequirementsOperationStatus status, RequirementRevisionDto? revision = null,
        IEnumerable<RequirementValidationIssue>? issues = null)
    {
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        var copy = issues?.ToArray() ?? [];
        if (copy.Any(item => item is null || string.IsNullOrWhiteSpace(item.Code)
                || string.IsNullOrWhiteSpace(item.Field) || string.IsNullOrWhiteSpace(item.Message)))
            throw new ArgumentException("Issues must have complete structural metadata.", nameof(issues));
        if (status == RequirementsOperationStatus.Success ? revision is null : revision is not null)
            throw new ArgumentException("Only success requires and may contain a revision.", nameof(revision));
        var requiresIssues = status is RequirementsOperationStatus.ValidationFailed or RequirementsOperationStatus.CatalogueConflict;
        if (requiresIssues != (copy.Length > 0))
            throw new ArgumentException("Only validation and catalogue conflicts require and may contain issues.", nameof(issues));
        if (status == RequirementsOperationStatus.CatalogueConflict && copy.Any(item => item.Code != "CATALOGUE_VERSION_MISMATCH"))
            throw new ArgumentException("Catalogue conflict requires catalogue-version issues.", nameof(issues));
        Status = status;
        Revision = revision;
        Issues = Array.AsReadOnly(copy);
    }
}
