namespace BuildMind.Domain.Entities;

/// <summary>Immutable evidence of explicit consent for selected items and one recipient.
/// Structural validity is not proof of authorization, recipient eligibility or policy approval.</summary>
public sealed class ShareConsent
{
    public Guid Id { get; }
    public Guid RequestId { get; }
    public Guid ProjectId { get; }
    public Guid GrantedBy { get; }
    public Guid RecipientId { get; }
    public Guid? ReportId { get; }
    public IReadOnlyList<Guid> DocumentIds { get; }
    public string PolicyVersion { get; }
    public DateTimeOffset GrantedAtUtc { get; }

    public ShareConsent(Guid id, Guid requestId, Guid projectId, Guid grantedBy, Guid recipientId,
        Guid? reportId, IEnumerable<Guid> documentIds, string policyVersion,
        DateTimeOffset grantedAtUtc, bool explicitlyGranted)
    {
        if (!explicitlyGranted) throw new ArgumentException("Explicit consent is required.", nameof(explicitlyGranted));
        if (id == Guid.Empty || requestId == Guid.Empty || projectId == Guid.Empty
            || grantedBy == Guid.Empty || recipientId == Guid.Empty || reportId == Guid.Empty)
            throw new ArgumentException("Nonempty identities are required.");
        ArgumentNullException.ThrowIfNull(documentIds);
        ArgumentException.ThrowIfNullOrWhiteSpace(policyVersion);
        var docs = documentIds.ToArray();
        if (docs.Any(x => x == Guid.Empty) || docs.Distinct().Count() != docs.Length)
            throw new ArgumentException("Document identities must be nonempty and unique.", nameof(documentIds));
        if (reportId is null && docs.Length == 0)
            throw new ArgumentException("At least one explicitly selected item is required.");
        Id = id;
        RequestId = requestId;
        ProjectId = projectId;
        GrantedBy = grantedBy;
        RecipientId = recipientId;
        ReportId = reportId;
        DocumentIds = Array.AsReadOnly(docs);
        PolicyVersion = policyVersion;
        GrantedAtUtc = grantedAtUtc.ToUniversalTime();
    }
}
