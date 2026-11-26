using BuildMind.Domain.Entities;

namespace BuildMind.Application.Features.ArchitectRequests.Contracts;

/// <summary>Candidate input contract only. No caller-supplied owner, request ID, timestamps or status.
/// Policy version is the version acknowledged by the caller, not proof it is currently approved.</summary>
public sealed class CreateArchitectRequestDto
{
    public Guid RecipientId { get; }
    public Guid? ReportId { get; }
    public IReadOnlyList<Guid>? DocumentIds { get; }
    public bool ExplicitConsent { get; }
    public string? AcknowledgedPolicyVersion { get; }

    public CreateArchitectRequestDto(Guid recipientId, Guid? reportId, IEnumerable<Guid>? documentIds,
        bool explicitConsent, string? acknowledgedPolicyVersion)
    {
        RecipientId = recipientId;
        ReportId = reportId;
        DocumentIds = documentIds is null ? null : Array.AsReadOnly(documentIds.ToArray());
        ExplicitConsent = explicitConsent;
        AcknowledgedPolicyVersion = acknowledgedPolicyVersion;
    }
}

public sealed record WithdrawArchitectRequestDto(long ExpectedStateVersion);

/// <summary>Internal candidate read model. Application must authorize access before mapping or returning.</summary>
public sealed class ArchitectRequestDto
{
    public Guid Id { get; }
    public Guid ProjectId { get; }
    public Guid RecipientId { get; }
    public Guid? ReportId { get; }
    public IReadOnlyList<Guid> DocumentIds { get; }
    public string PolicyVersion { get; }
    public DateTimeOffset ConsentedAtUtc { get; }
    public DateTimeOffset RequestedAtUtc { get; }
    public long StateVersion { get; }
    public ArchitectRequestStatus Status { get; }
    public DateTimeOffset? WithdrawnAtUtc { get; }

    public ArchitectRequestDto(ArchitectRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        Id = request.Id;
        ProjectId = request.ProjectId;
        RecipientId = request.RecipientId;
        ReportId = request.Consent.ReportId;
        DocumentIds = Array.AsReadOnly(request.Consent.DocumentIds.ToArray());
        PolicyVersion = request.Consent.PolicyVersion;
        ConsentedAtUtc = request.Consent.GrantedAtUtc;
        RequestedAtUtc = request.RequestedAtUtc;
        StateVersion = request.StateVersion;
        Status = request.Status;
        WithdrawnAtUtc = request.WithdrawnAtUtc;
    }
}
