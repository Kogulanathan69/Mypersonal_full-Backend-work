using BuildMind.Application.Features.Requirements.Contracts;
using BuildMind.Application.Features.Requirements.Integration;

namespace BuildMind.Application.Features.Requirements.Services;

/// <summary>Isolated evaluator over a trusted consistent context. No persistence writes,
/// job creation, public route or production registration. Diagnostic codes are provisional.</summary>
public sealed class RequirementReadinessService : IRequirementReadinessSource
{
    private readonly IRequirementReadinessContextProvider _provider;
    private readonly TimeProvider _clock;

    public RequirementReadinessService(IRequirementReadinessContextProvider provider, TimeProvider clock)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
    }

    public async Task<RequirementReadinessResult> GetAsync(Guid projectId, Guid authenticatedUserId,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (projectId == Guid.Empty || authenticatedUserId == Guid.Empty)
            return Unavailable("READINESS_CONTEXT_UNAVAILABLE");

        // Unknown faults propagate; never retry or fabricate a ready result.
        var context = await _provider.GetAsync(projectId, authenticatedUserId, cancellationToken)
            .ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (context is null || context.ProjectId != projectId || context.AuthenticatedUserId != authenticatedUserId)
            return Unavailable("READINESS_CONTEXT_UNAVAILABLE");

        var revision = context.CurrentRequirements;
        if (context.ConfirmedLandRevisionId == Guid.Empty || (revision is not null && revision.ProjectId != projectId))
            return Unavailable("INVALID_READINESS_CONTEXT");
        if (context.Catalogue is null || context.IsGenerationEligible is null)
            return Unavailable("READINESS_POLICY_UNAVAILABLE");
        if (!context.Catalogue.DesignMethodPolicyAvailable)
            return Unavailable("DESIGN_METHOD_POLICY_UNAVAILABLE");

        var codes = new List<string>();
        if (context.IsGenerationEligible == false) codes.Add("PROJECT_NOT_ELIGIBLE");
        if (context.ConfirmedLandRevisionId is null) codes.Add("CONFIRMED_LAND_REQUIRED");
        if (revision is null) codes.Add("REQUIREMENTS_REQUIRED");
        else
        {
            var dto = new RequirementRevisionDto(revision);
            if (!string.Equals(dto.SchemaVersion, context.Catalogue.SchemaVersion, StringComparison.Ordinal))
                codes.Add("REQUIREMENTS_SCHEMA_MISMATCH");
            var request = new SaveRequirementsRequest(dto.RevisionId, dto.CatalogueVersion, dto.FloorCount,
                dto.GroundFloorRooms, dto.AdditionalSpaces, dto.Relationships, dto.DesignMethod);
            var validation = RequirementContractValidator.Validate(request, context.Catalogue);
            if (validation.Status == RequirementValidationStatus.PolicyUnavailable)
                return Unavailable("READINESS_POLICY_UNAVAILABLE");
            if (validation.Status != RequirementValidationStatus.Valid)
                codes.AddRange(validation.Issues.Select(issue => $"{issue.Code}:{issue.Field}"));
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (codes.Count != 0)
            return new(RequirementReadinessStatus.Incomplete, null, codes.Distinct(StringComparer.Ordinal));

        var capturedAtUtc = _clock.GetUtcNow();
        cancellationToken.ThrowIfCancellationRequested();
        return new(RequirementReadinessStatus.Ready,
            new RequirementReadinessSnapshot(projectId, context.ConfirmedLandRevisionId!.Value,
                revision!, capturedAtUtc), []);
    }

    private static RequirementReadinessResult Unavailable(string code) =>
        new(RequirementReadinessStatus.Unavailable, null, [code]);
}
