using BuildMind.Domain.Entities;

namespace BuildMind.Domain.ValueObjects;

/// <summary>
/// Immutable report inputs. Structural validity does not prove persisted confirmation, source trust,
/// supported-schema eligibility or authorization. This internal model is not a customer response DTO.
/// </summary>
public sealed class ReportSnapshot
{
    public string SchemaVersion { get; }
    public DateTimeOffset CapturedAtUtc { get; }
    public Guid ProjectId => Requirements.ProjectId;
    public RequirementRevision Requirements { get; }
    public ReportPlanSnapshot Plan { get; }
    public ReportConfirmationSnapshot Confirmation { get; }
    public ReportDisclaimer Disclaimer { get; }

    public ReportSnapshot(string schemaVersion, DateTimeOffset capturedAtUtc,
        RequirementRevision requirements, ReportPlanSnapshot plan,
        ReportConfirmationSnapshot confirmation, ReportDisclaimer disclaimer)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(schemaVersion);
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(confirmation);
        ArgumentNullException.ThrowIfNull(disclaimer);
        if (requirements.ProjectId != plan.ProjectId || confirmation.ProjectId != plan.ProjectId
            || requirements.Id != plan.RequirementsRevisionId || confirmation.PlanId != plan.PlanId
            || confirmation.ConfirmedRevisionId != plan.RevisionId
            || confirmation.ValidationId != plan.Validation.ValidationId
            || !string.Equals(confirmation.GeometryHash, plan.GeometryHash, StringComparison.Ordinal))
            throw new ArgumentException("Report source identities, revisions or confirmation evidence conflict.");
        SchemaVersion = schemaVersion;
        CapturedAtUtc = capturedAtUtc.ToUniversalTime();
        Requirements = requirements; // Sealed immutable Domain snapshot, including defensive collection copies.
        Plan = plan;
        Confirmation = confirmation;
        Disclaimer = disclaimer;
    }
}

public sealed class ReportPlanSnapshot
{
    public Guid ProjectId { get; }
    public Guid PlanId { get; }
    public Guid RevisionId { get; }
    public long VersionNumber { get; }
    public string GeometrySchemaVersion { get; }
    public string GeometryJson { get; }
    public string GeometryHash { get; }
    public Guid LandRevisionId { get; }
    public Guid RequirementsRevisionId { get; }
    public Guid CreatedBy { get; }
    public DateTimeOffset CreatedAtUtc { get; }
    public ReportValidationSnapshot Validation { get; }

    /// <summary>JSON parsing and recomputation of the M3 geometry hash belong to the Application boundary.</summary>
    public ReportPlanSnapshot(Guid projectId, Guid planId, Guid revisionId, long versionNumber,
        string geometrySchemaVersion, string geometryJson, string geometryHash, Guid landRevisionId,
        Guid requirementsRevisionId, Guid createdBy, DateTimeOffset createdAtUtc, ReportValidationSnapshot validation)
    {
        ReportStructure.RequireIdentity(projectId, nameof(projectId));
        ReportStructure.RequireIdentity(planId, nameof(planId));
        ReportStructure.RequireIdentity(revisionId, nameof(revisionId));
        ReportStructure.RequireIdentity(landRevisionId, nameof(landRevisionId));
        ReportStructure.RequireIdentity(requirementsRevisionId, nameof(requirementsRevisionId));
        ReportStructure.RequireIdentity(createdBy, nameof(createdBy));
        if (versionNumber <= 0) throw new ArgumentOutOfRangeException(nameof(versionNumber));
        ArgumentException.ThrowIfNullOrWhiteSpace(geometrySchemaVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(geometryJson);
        ReportStructure.RequireGeometryHash(geometryHash, nameof(geometryHash));
        ArgumentNullException.ThrowIfNull(validation);
        if (!string.Equals(geometryHash, validation.GeometryHash, StringComparison.Ordinal))
            throw new ArgumentException("Validation must reference the exact plan geometry hash.", nameof(validation));
        ProjectId = projectId;
        PlanId = planId;
        RevisionId = revisionId;
        VersionNumber = versionNumber;
        GeometrySchemaVersion = geometrySchemaVersion;
        GeometryJson = geometryJson;
        GeometryHash = geometryHash;
        LandRevisionId = landRevisionId;
        RequirementsRevisionId = requirementsRevisionId;
        CreatedBy = createdBy;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
        Validation = validation;
    }
}

public sealed class ReportValidationSnapshot
{
    public Guid ValidationId { get; }
    public string ValidatorVersion { get; }
    public string GeometryHash { get; }
    public bool GeometryValid { get; }
    public bool? RequirementsMatch { get; }
    public string RegulatoryEvidenceStatus { get; }
    public bool HasBlockingIssues { get; }
    public DateTimeOffset ValidatedAtUtc { get; }

    public ReportValidationSnapshot(Guid validationId, string validatorVersion, string geometryHash,
        bool geometryValid, bool? requirementsMatch, string regulatoryEvidenceStatus,
        bool hasBlockingIssues, DateTimeOffset validatedAtUtc)
    {
        ReportStructure.RequireIdentity(validationId, nameof(validationId));
        ArgumentException.ThrowIfNullOrWhiteSpace(validatorVersion);
        ArgumentException.ThrowIfNullOrWhiteSpace(regulatoryEvidenceStatus);
        ReportStructure.RequireGeometryHash(geometryHash, nameof(geometryHash));
        ValidationId = validationId;
        ValidatorVersion = validatorVersion;
        GeometryHash = geometryHash;
        GeometryValid = geometryValid;
        RequirementsMatch = requirementsMatch;
        RegulatoryEvidenceStatus = regulatoryEvidenceStatus; // No conversion of unknown evidence to verified.
        HasBlockingIssues = hasBlockingIssues;
        ValidatedAtUtc = validatedAtUtc.ToUniversalTime();
    }
}

public sealed class ReportConfirmationSnapshot
{
    public Guid ConfirmationId { get; }
    public Guid ProjectId { get; }
    public Guid PlanId { get; }
    public Guid ConfirmedRevisionId { get; }
    public string GeometryHash { get; }
    public Guid ValidationId { get; }
    public Guid ConfirmedBy { get; }
    public DateTimeOffset ConfirmedAtUtc { get; }
    public bool WarningAcknowledgement { get; }

    public ReportConfirmationSnapshot(Guid confirmationId, Guid projectId, Guid planId, Guid confirmedRevisionId,
        string geometryHash, Guid validationId, Guid confirmedBy, DateTimeOffset confirmedAtUtc, bool warningAcknowledgement)
    {
        ReportStructure.RequireIdentity(confirmationId, nameof(confirmationId));
        ReportStructure.RequireIdentity(projectId, nameof(projectId));
        ReportStructure.RequireIdentity(planId, nameof(planId));
        ReportStructure.RequireIdentity(confirmedRevisionId, nameof(confirmedRevisionId));
        ReportStructure.RequireIdentity(validationId, nameof(validationId));
        ReportStructure.RequireIdentity(confirmedBy, nameof(confirmedBy));
        ReportStructure.RequireGeometryHash(geometryHash, nameof(geometryHash));
        ConfirmationId = confirmationId;
        ProjectId = projectId;
        PlanId = planId;
        ConfirmedRevisionId = confirmedRevisionId;
        GeometryHash = geometryHash;
        ValidationId = validationId;
        ConfirmedBy = confirmedBy;
        ConfirmedAtUtc = confirmedAtUtc.ToUniversalTime();
        WarningAcknowledgement = warningAcknowledgement;
    }
}

/// <summary>Caller-supplied versioned wording. Construction does not certify product/legal approval.</summary>
public sealed class ReportDisclaimer
{
    public string Version { get; }
    public string Text { get; }

    public ReportDisclaimer(string version, string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        Version = version;
        Text = text;
    }
}

/// <summary>Internal metadata only. ArtifactId is opaque, never a public URL or filesystem path.</summary>
public sealed class ReportArtifact
{
    public Guid ArtifactId { get; }
    public string MediaType { get; }
    public long ByteLength { get; }
    public string ContentHash { get; }
    public DateTimeOffset CreatedAtUtc { get; }

    public ReportArtifact(Guid artifactId, string mediaType, long byteLength, string contentHash, DateTimeOffset createdAtUtc)
    {
        ReportStructure.RequireIdentity(artifactId, nameof(artifactId));
        ArgumentException.ThrowIfNullOrWhiteSpace(mediaType);
        ArgumentException.ThrowIfNullOrWhiteSpace(contentHash);
        if (byteLength <= 0) throw new ArgumentOutOfRangeException(nameof(byteLength));
        ArtifactId = artifactId;
        MediaType = mediaType; // Renderer/storage contract will define supported media types and content-hash convention.
        ByteLength = byteLength;
        ContentHash = contentHash;
        CreatedAtUtc = createdAtUtc.ToUniversalTime();
    }
}

internal static class ReportStructure
{
    public static void RequireIdentity(Guid value, string parameterName)
    {
        if (value == Guid.Empty) throw new ArgumentException("Identity is required.", parameterName);
    }

    // Existing M3 convention: sha256: followed by 64 lowercase hexadecimal characters.
    public static void RequireGeometryHash(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (value.Length != 71 || !value.StartsWith("sha256:", StringComparison.Ordinal)
            || value.AsSpan(7).ContainsAnyExcept("0123456789abcdef"))
            throw new ArgumentException("Geometry hash must use the M3 SHA-256 convention.", parameterName);
    }
}
