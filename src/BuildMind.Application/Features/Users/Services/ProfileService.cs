using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Users.Abstractions;
using BuildMind.Application.Features.Users.DTOs;
using BuildMind.Application.Features.Users.Validators;

namespace BuildMind.Application.Features.Users.Services;

public enum ProfileOperationStatus
{
    Success,
    Unauthenticated,
    ValidationFailed,
    NotFound
}

public sealed record ProfileOperationResult(
    ProfileOperationStatus Status,
    ProfileResponse? Value = null);

public sealed class ProfileService(
    IUserProfileRepository repository,
    ICurrentUser currentUser)
{
    private Guid? UserId =>
        currentUser.IsAuthenticated &&
        currentUser.UserId is Guid id &&
        id != Guid.Empty ? id : null;

    public async Task<ProfileOperationResult> GetAsync(
        CancellationToken cancellationToken)
    {
        if (UserId is not Guid id)
            return new(ProfileOperationStatus.Unauthenticated);

        var profile = await repository.GetAsync(
            id, cancellationToken);

        if (profile is null ||
            !profile.IsActive ||
            profile.Id != id)
        {
            return new(ProfileOperationStatus.NotFound);
        }

        return new(
            ProfileOperationStatus.Success, profile);
    }

    public async Task<ProfileOperationResult> UpdateAsync(
        UpdateProfileRequest request,
        CancellationToken cancellationToken)
    {
        if (UserId is not Guid id)
            return new(ProfileOperationStatus.Unauthenticated);

        if (!new UpdateProfileRequestValidator()
            .Validate(request).IsValid)
        {
            return new(ProfileOperationStatus.ValidationFailed);
        }

        var profile = await repository.UpdateAsync(
            id,
            request.FullName.Trim(),
            request.PhoneNumber,
            cancellationToken);

        if (profile is null ||
            !profile.IsActive ||
            profile.Id != id)
        {
            return new(ProfileOperationStatus.NotFound);
        }

        return new(
            ProfileOperationStatus.Success, profile);
    }
}