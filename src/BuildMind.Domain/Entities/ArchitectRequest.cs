namespace BuildMind.Domain.Entities;

// Provisional internal lifecycle. External acceptance/decline and withdrawal eligibility await G12.
public enum ArchitectRequestStatus { Pending, Withdrawn, Completed }

/// <summary>Immutable assistance request. Neither creation nor withdrawal dispatches any documents.</summary>
public sealed class ArchitectRequest
{
    public Guid Id => Consent.RequestId;
    public Guid ProjectId => Consent.ProjectId;
    public Guid RequestedBy => Consent.GrantedBy;
    public Guid RecipientId => Consent.RecipientId;
    public ShareConsent Consent { get; }
    public DateTimeOffset RequestedAtUtc { get; }
    public long StateVersion { get; }
    public ArchitectRequestStatus Status { get; }
    public DateTimeOffset? WithdrawnAtUtc { get; }

    private ArchitectRequest(ShareConsent consent, DateTimeOffset requestedAtUtc, long stateVersion,
        ArchitectRequestStatus status, DateTimeOffset? withdrawnAtUtc)
    {
        ArgumentNullException.ThrowIfNull(consent);
        if (requestedAtUtc < consent.GrantedAtUtc)
            throw new ArgumentException("Request cannot precede its consent.", nameof(requestedAtUtc));
        if (!Enum.IsDefined(status)) throw new ArgumentOutOfRangeException(nameof(status));
        if (status == ArchitectRequestStatus.Pending && (stateVersion != 1 || withdrawnAtUtc is not null))
            throw new ArgumentException("Pending requests require initial state.");
        if (status == ArchitectRequestStatus.Withdrawn && (stateVersion != 2 || withdrawnAtUtc is null || withdrawnAtUtc.Value < requestedAtUtc))
            throw new ArgumentException("Withdrawn requests require a valid withdrawal time and terminal version.");
        Consent = consent;
        RequestedAtUtc = requestedAtUtc.ToUniversalTime();
        StateVersion = stateVersion;
        Status = status;
        WithdrawnAtUtc = withdrawnAtUtc?.ToUniversalTime();
    }

    public static ArchitectRequest CreatePending(ShareConsent consent, DateTimeOffset requestedAtUtc) =>
        new(consent, requestedAtUtc, 1, ArchitectRequestStatus.Pending, null);

    /// <summary>Structural rehydration only; does not authenticate or establish persisted history.</summary>
    public static ArchitectRequest Rehydrate(ShareConsent consent, DateTimeOffset requestedAtUtc,
        long stateVersion, ArchitectRequestStatus status, DateTimeOffset? withdrawnAtUtc = null) =>
        new(consent, requestedAtUtc, stateVersion, status, withdrawnAtUtc);

    /// <summary>Builds a candidate only after a trusted caller establishes withdrawal eligibility.
    /// This method does not establish authorization or an approved production policy.</summary>
    public ArchitectRequest ProposeWithdrawal(long expectedStateVersion, DateTimeOffset withdrawnAtUtc)
    {
        if (expectedStateVersion != StateVersion || Status != ArchitectRequestStatus.Pending)
            throw new InvalidOperationException("Request state conflict or unsupported transition.");
        return new(Consent, RequestedAtUtc, checked(StateVersion + 1), ArchitectRequestStatus.Withdrawn, withdrawnAtUtc);
    }
}
