
namespace BuildMind.Domain.ValueObjects.Land;

public enum LandLocationState
{
    Missing,
    Provided
}

public enum LandLocationSource
{
    UserSelected,
    SurveyDocument,
    GeocodingProvider
}

public enum LandLocationVerificationStatus
{
    NotVerified,
    Verified
}

/// <summary>
/// Stores land locality and optional WGS84 coordinates.
/// Location verification is recorded only when an approved
/// geocoding provider has actually verified the location.
/// </summary>
public sealed class LandLocation
{
    public LandLocationState State { get; }
    public string? LocalityId { get; }
    public string? LocalityName { get; }
    public double? Latitude { get; }
    public double? Longitude { get; }
    public LandLocationSource? Source { get; }
    public LandLocationVerificationStatus VerificationStatus { get; }
    public string? VerificationProvider { get; }
    public DateTimeOffset? VerifiedAtUtc { get; }

    private LandLocation(
        LandLocationState state,
        string? localityId,
        string? localityName,
        double? latitude,
        double? longitude,
        LandLocationSource? source,
        LandLocationVerificationStatus verificationStatus,
        string? verificationProvider,
        DateTimeOffset? verifiedAtUtc)
    {
        State = state;
        LocalityId = localityId;
        LocalityName = localityName;
        Latitude = latitude;
        Longitude = longitude;
        Source = source;
        VerificationStatus = verificationStatus;
        VerificationProvider = verificationProvider;
        VerifiedAtUtc = verifiedAtUtc;
    }

    public static LandLocation Missing() =>
        new(
            LandLocationState.Missing,
            null,
            null,
            null,
            null,
            null,
            LandLocationVerificationStatus.NotVerified,
            null,
            null);

    public static LandLocation Create(
        string localityName,
        LandLocationSource source,
        string? localityId = null,
        double? latitude = null,
        double? longitude = null,
        LandLocationVerificationStatus verificationStatus =
            LandLocationVerificationStatus.NotVerified,
        string? verificationProvider = null,
        DateTimeOffset? verifiedAtUtc = null)
    {
        if (string.IsNullOrWhiteSpace(localityName))
            throw new ArgumentException(
                "Locality name is required.", nameof(localityName));

        localityName = localityName.Trim();

        if (localityName.Length > 200)
            throw new ArgumentException(
                "Locality name cannot exceed 200 characters.",
                nameof(localityName));

        if (localityId is { Length: > 100 })
            throw new ArgumentException(
                "Locality ID cannot exceed 100 characters.",
                nameof(localityId));

        if (localityId is not null)
        {
            localityId = localityId.Trim();

            if (localityId.Length == 0)
                throw new ArgumentException(
                    "Locality ID cannot be blank.", nameof(localityId));
        }

        if (!Enum.IsDefined(source))
            throw new ArgumentOutOfRangeException(nameof(source));

        if (!Enum.IsDefined(verificationStatus))
            throw new ArgumentOutOfRangeException(
                nameof(verificationStatus));

        if (latitude.HasValue != longitude.HasValue)
            throw new ArgumentException(
                "Latitude and longitude must be provided together.");

        if (latitude is double lat &&
            (!double.IsFinite(lat) || lat < -90 || lat > 90))
            throw new ArgumentOutOfRangeException(nameof(latitude));

        if (longitude is double lon &&
            (!double.IsFinite(lon) || lon < -180 || lon > 180))
            throw new ArgumentOutOfRangeException(nameof(longitude));

        if (verificationStatus ==
            LandLocationVerificationStatus.Verified)
        {
            if (source != LandLocationSource.GeocodingProvider)
                throw new ArgumentException(
                    "Only a geocoding provider can verify a location.",
                    nameof(source));

            if (string.IsNullOrWhiteSpace(verificationProvider))
                throw new ArgumentException(
                    "Verification provider is required.",
                    nameof(verificationProvider));

            if (verificationProvider.Trim().Length > 100)
                throw new ArgumentException(
                    "Verification provider cannot exceed 100 characters.",
                    nameof(verificationProvider));

            if (verifiedAtUtc is null)
                throw new ArgumentException(
                    "Verification timestamp is required.",
                    nameof(verifiedAtUtc));

            verificationProvider = verificationProvider.Trim();
            verifiedAtUtc = verifiedAtUtc.Value.ToUniversalTime();
        }
        else if (verificationProvider is not null ||
                 verifiedAtUtc is not null)
        {
            throw new ArgumentException(
                "Unverified locations cannot contain verification metadata.");
        }

        return new LandLocation(
            LandLocationState.Provided,
            localityId,
            localityName,
            latitude,
            longitude,
            source,
            verificationStatus,
            verificationProvider,
            verifiedAtUtc);
    }
}
