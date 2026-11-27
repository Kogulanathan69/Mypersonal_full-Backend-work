
using BuildMind.Domain.ValueObjects.Land;

namespace BuildMind.Requirements.Tests.Domain;

public sealed class LandLocationTests
{
    [Fact]
    public void Missing_CreatesExplicitMissingLocation()
    {
        var location = LandLocation.Missing();

        Assert.Equal(LandLocationState.Missing, location.State);
        Assert.Null(location.LocalityName);
        Assert.Null(location.Latitude);
        Assert.Null(location.Longitude);
        Assert.Equal(
            LandLocationVerificationStatus.NotVerified,
            location.VerificationStatus);
    }

    [Fact]
    public void Create_PreservesUserSelectedLocation()
    {
        var location = LandLocation.Create(
            "  Negombo  ",
            LandLocationSource.UserSelected,
            localityId: "NEG-001",
            latitude: 7.2088,
            longitude: 79.8358);

        Assert.Equal(LandLocationState.Provided, location.State);
        Assert.Equal("Negombo", location.LocalityName);
        Assert.Equal("NEG-001", location.LocalityId);
        Assert.Equal(7.2088, location.Latitude);
        Assert.Equal(79.8358, location.Longitude);
        Assert.Equal(
            LandLocationSource.UserSelected,
            location.Source);
        Assert.Equal(
            LandLocationVerificationStatus.NotVerified,
            location.VerificationStatus);
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-90.01)]
    [InlineData(90.01)]
    public void Create_RejectsInvalidLatitude(double latitude)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LandLocation.Create(
                "Negombo",
                LandLocationSource.UserSelected,
                latitude: latitude,
                longitude: 79.8358));
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    [InlineData(-180.01)]
    [InlineData(180.01)]
    public void Create_RejectsInvalidLongitude(double longitude)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LandLocation.Create(
                "Negombo",
                LandLocationSource.UserSelected,
                latitude: 7.2088,
                longitude: longitude));
    }

    [Fact]
    public void Create_RejectsOnlyOneCoordinate()
    {
        Assert.Throws<ArgumentException>(() =>
            LandLocation.Create(
                "Negombo",
                LandLocationSource.UserSelected,
                latitude: 7.2088));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void Create_RejectsMissingLocalityName(string? name)
    {
        Assert.Throws<ArgumentException>(() =>
            LandLocation.Create(
                name!,
                LandLocationSource.UserSelected));
    }

    [Fact]
    public void Create_RejectsVerifiedLocationWithoutGeocodingProvider()
    {
        Assert.Throws<ArgumentException>(() =>
            LandLocation.Create(
                "Negombo",
                LandLocationSource.UserSelected,
                verificationStatus:
                    LandLocationVerificationStatus.Verified,
                verificationProvider: "Provider",
                verifiedAtUtc: DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Create_RejectsVerifiedLocationWithoutProviderName()
    {
        Assert.Throws<ArgumentException>(() =>
            LandLocation.Create(
                "Negombo",
                LandLocationSource.GeocodingProvider,
                verificationStatus:
                    LandLocationVerificationStatus.Verified,
                verifiedAtUtc: DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Create_RejectsVerifiedLocationWithoutTimestamp()
    {
        Assert.Throws<ArgumentException>(() =>
            LandLocation.Create(
                "Negombo",
                LandLocationSource.GeocodingProvider,
                verificationStatus:
                    LandLocationVerificationStatus.Verified,
                verificationProvider: "ApprovedProvider"));
    }

    [Fact]
    public void Create_AcceptsVerifiedLocationWithProviderEvidence()
    {
        var timestamp = new DateTimeOffset(
            2026, 11, 24, 10, 0, 0, TimeSpan.FromHours(5.5));

        var location = LandLocation.Create(
            "Negombo",
            LandLocationSource.GeocodingProvider,
            latitude: 7.2088,
            longitude: 79.8358,
            verificationStatus:
                LandLocationVerificationStatus.Verified,
            verificationProvider: "ApprovedProvider",
            verifiedAtUtc: timestamp);

        Assert.Equal(
            LandLocationVerificationStatus.Verified,
            location.VerificationStatus);
        Assert.Equal("ApprovedProvider", location.VerificationProvider);
        Assert.Equal(TimeSpan.Zero, location.VerifiedAtUtc!.Value.Offset);
    }

    [Fact]
    public void Create_RejectsVerificationMetadataForUnverifiedLocation()
    {
        Assert.Throws<ArgumentException>(() =>
            LandLocation.Create(
                "Negombo",
                LandLocationSource.UserSelected,
                verificationProvider: "Provider"));
    }
}
