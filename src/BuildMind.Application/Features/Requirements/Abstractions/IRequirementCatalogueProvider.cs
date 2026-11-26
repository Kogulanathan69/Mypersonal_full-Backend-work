using BuildMind.Application.Features.Requirements.Contracts;

namespace BuildMind.Application.Features.Requirements.Abstractions;

/// <summary>Provisional M5 port for isolated development under D04; no production provider exists.</summary>
public interface IRequirementCatalogueProvider
{
    /// <summary>
    /// Resolve the authoritative policy for this project independently of client-supplied versions.
    /// Null means unavailable, not a configured empty policy. Construction does not certify approval.
    /// </summary>
    Task<RequirementCatalogue?> GetForProjectAsync(Guid projectId, CancellationToken cancellationToken);
}
