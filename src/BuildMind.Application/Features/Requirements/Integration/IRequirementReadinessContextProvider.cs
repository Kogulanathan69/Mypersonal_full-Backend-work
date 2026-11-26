using BuildMind.Application.Features.Requirements.Contracts;
using BuildMind.Domain.Entities;

namespace BuildMind.Application.Features.Requirements.Integration;

/// <summary>Provisional internal boundary; no production implementation is supplied.
/// The provider must establish trusted actor identity, authorize ownership and read the current
/// confirmed land, current requirements, applicable approved policy and generation eligibility
/// as one consistent observation. Null hides absent/unauthorized/unavailable context.
/// Null eligibility means unknown; null land or requirements means authorized absence.
/// Consumers must atomically recheck revisions and authorization before generation.</summary>
public interface IRequirementReadinessContextProvider
{
    Task<RequirementReadinessContext?> GetAsync(Guid projectId, Guid authenticatedUserId,
        CancellationToken cancellationToken);
}

public sealed record RequirementReadinessContext(
    Guid ProjectId,
    Guid AuthenticatedUserId,
    Guid? ConfirmedLandRevisionId,
    RequirementRevision? CurrentRequirements,
    RequirementCatalogue? Catalogue,
    bool? IsGenerationEligible);
