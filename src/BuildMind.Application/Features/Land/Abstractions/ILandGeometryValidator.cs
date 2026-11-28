using BuildMind.Application.Features.Land.Contracts;

namespace BuildMind.Application.Features.Land.Abstractions;

public interface ILandGeometryValidator
{
    Task<LandGeometryValidationResult> ValidateAsync(
        LandGeometryValidationRequest request,
        CancellationToken cancellationToken);
}
