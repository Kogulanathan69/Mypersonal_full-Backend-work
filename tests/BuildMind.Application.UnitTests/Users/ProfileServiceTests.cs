using BuildMind.Application.Abstractions;
using BuildMind.Application.Features.Users.Abstractions;
using BuildMind.Application.Features.Users.DTOs;
using BuildMind.Application.Features.Users.Services;
using Xunit;

namespace BuildMind.Application.UnitTests.Users;

public sealed class ProfileServiceTests
{
    private readonly Guid _userId = Guid.NewGuid();

    private readonly FakeRepository _repository = new();

    private ProfileService CreateService(bool authenticated = true, Guid? subject = null)
        => new(
            _repository,
            new FakeCurrentUser(authenticated ? subject ?? _userId : null));

    private void Seed(bool active = true, Guid? id = null)
    {
        _repository.Profile = new ProfileResponse
        {
            Id = id ?? _userId,
            Email = "customer@example.com",
            FullName = "Test Customer",
            PhoneNumber = "+94771234567",
            IsActive = active,
            CreatedAtUtc = DateTimeOffset.UtcNow
        };
    }

    private static UpdateProfileRequest Request(
        string name = "Updated Customer",
        string? phone = "+94771234567")
        => new()
        {
            FullName = name,
            PhoneNumber = phone
        };

    [Fact]
    public async Task AuthenticatedUser_CanReadProfile()
    {
        Seed();

        var result = await CreateService().GetAsync(default);

        Assert.Equal(ProfileOperationStatus.Success, result.Status);
        Assert.Equal(_userId, result.Value!.Id);
    }

    [Fact]
    public async Task AnonymousUser_CannotReadProfile()
    {
        var result = await CreateService(false).GetAsync(default);

        Assert.Equal(
            ProfileOperationStatus.Unauthenticated, result.Status);

        Assert.Equal(0, _repository.ReadCount);
    }

    [Fact]
    public async Task UnknownProfile_ShouldReturnNotFound()
    {
        var result = await CreateService().GetAsync(default);

        Assert.Equal(ProfileOperationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task EmptyAuthenticatedSubject_CannotReadOrUpdateProfile()
    {
        Seed();
        var service = CreateService(subject: Guid.Empty);

        var read = await service.GetAsync(default);
        var update = await service.UpdateAsync(Request(), default);

        Assert.Equal(ProfileOperationStatus.Unauthenticated, read.Status);
        Assert.Equal(ProfileOperationStatus.Unauthenticated, update.Status);
        Assert.Equal(0, _repository.ReadCount);
        Assert.Equal(0, _repository.UpdateCount);
    }

    [Fact]
    public async Task InactiveUser_CannotReadProfile()
    {
        Seed(active: false);

        var result = await CreateService().GetAsync(default);

        Assert.Equal(ProfileOperationStatus.NotFound, result.Status);
    }

    [Fact]
    public async Task CrossAccountProfile_ShouldBeRejected()
    {
        Seed(id: Guid.NewGuid());

        var result = await CreateService().GetAsync(default);

        Assert.Equal(ProfileOperationStatus.NotFound, result.Status);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task ValidProfileUpdate_ShouldSucceed()
    {
        Seed();

        var result = await CreateService().UpdateAsync(
            Request(), default);

        Assert.Equal(ProfileOperationStatus.Success, result.Status);
        Assert.Equal("Updated Customer", result.Value!.FullName);
        Assert.Equal(_userId, _repository.LastUpdatedUserId);
    }

    [Fact]
    public async Task AnonymousUser_CannotUpdateProfile()
    {
        var result = await CreateService(false).UpdateAsync(
            Request(), default);

        Assert.Equal(
            ProfileOperationStatus.Unauthenticated, result.Status);

        Assert.Equal(0, _repository.UpdateCount);
    }

    [Fact]
    public async Task InvalidName_ShouldBeRejected()
    {
        Seed();

        var result = await CreateService().UpdateAsync(
            Request(name: ""), default);

        Assert.Equal(
            ProfileOperationStatus.ValidationFailed, result.Status);

        Assert.Equal(0, _repository.UpdateCount);
    }

    [Fact]
    public async Task InvalidPhone_ShouldBeRejected()
    {
        Seed();

        var result = await CreateService().UpdateAsync(
            Request(phone: "invalid"), default);

        Assert.Equal(
            ProfileOperationStatus.ValidationFailed, result.Status);

        Assert.Equal(0, _repository.UpdateCount);
    }

    [Fact]
    public async Task Name_ShouldBeTrimmed()
    {
        Seed();

        var result = await CreateService().UpdateAsync(
            Request(name: "  Test Customer  "), default);

        Assert.Equal(ProfileOperationStatus.Success, result.Status);
        Assert.Equal("Test Customer", result.Value!.FullName);
    }

    [Fact]
    public async Task PhoneNumber_CanBeCleared()
    {
        Seed();

        var result = await CreateService().UpdateAsync(
            Request(phone: null), default);

        Assert.Equal(ProfileOperationStatus.Success, result.Status);
        Assert.Null(result.Value!.PhoneNumber);
    }

    [Fact]
    public async Task InactiveUser_CannotUpdateProfile()
    {
        Seed(active: false);

        var result = await CreateService().UpdateAsync(
            Request(), default);

        Assert.Equal(ProfileOperationStatus.NotFound, result.Status);
        Assert.Equal("Test Customer", _repository.Profile!.FullName);
    }

    [Fact]
    public async Task CrossAccountUpdate_ShouldBeRejected()
    {
        Seed(id: Guid.NewGuid());

        var result = await CreateService().UpdateAsync(
            Request(), default);

        Assert.Equal(ProfileOperationStatus.NotFound, result.Status);
        Assert.Equal("Test Customer", _repository.Profile!.FullName);
    }

    private sealed class FakeCurrentUser(Guid? id)
        : ICurrentUser
    {
        public bool IsAuthenticated => id.HasValue;

        public Guid? UserId => id;

        public IReadOnlySet<string> Roles { get; }
            = new HashSet<string>();

        public Guid GetRequiredUserId()
            => id ?? throw new InvalidOperationException();
    }

    private sealed class FakeRepository : IUserProfileRepository
    {
        public ProfileResponse? Profile { get; set; }

        public int ReadCount { get; private set; }

        public int UpdateCount { get; private set; }

        public Guid? LastUpdatedUserId { get; private set; }

        public Task<ProfileResponse?> GetAsync(
            Guid userId,
            CancellationToken cancellationToken)
        {
            ReadCount++;

            return Task.FromResult(Profile);
        }

        public Task<ProfileResponse?> UpdateAsync(
            Guid userId,
            string fullName,
            string? phoneNumber,
            CancellationToken cancellationToken)
        {
            UpdateCount++;

            LastUpdatedUserId = userId;

            if (Profile is null ||
                Profile.Id != userId ||
                !Profile.IsActive)
            {
                return Task.FromResult<ProfileResponse?>(null);
            }

            Profile = new ProfileResponse
            {
                Id = Profile.Id,
                Email = Profile.Email,
                FullName = fullName,
                PhoneNumber = phoneNumber,
                IsActive = Profile.IsActive,
                CreatedAtUtc = Profile.CreatedAtUtc
            };

            return Task.FromResult<ProfileResponse?>(Profile);
        }
    }
}
