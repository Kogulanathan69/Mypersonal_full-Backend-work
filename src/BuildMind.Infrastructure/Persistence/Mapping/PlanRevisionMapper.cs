using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Infrastructure.Persistence.Records.PlanEditor;

namespace BuildMind.Infrastructure.Persistence.Mapping;

internal static class PlanRevisionMapper
{
    public static PlanRevisionSnapshot ToSnapshot(PlanRevisionRecord row) => new(
        row.RevisionId, row.PlanId, row.ProjectId, row.ParentRevisionId, row.VersionNumber,
        row.GeometrySchemaVersion, row.GeometryJson, row.GeometryHash, row.LandRevisionId,
        row.RequirementsRevisionId, new PlanValidationReferenceDto
        {
            ValidationId = row.ValidationId,
            ValidatorVersion = row.ValidatorVersion,
            GeometryHash = row.ValidationGeometryHash,
            GeometryValid = row.GeometryValid,
            RequirementsMatch = row.RequirementsMatch,
            RegulatoryEvidenceStatus = row.RegulatoryEvidenceStatus,
            HasBlockingIssues = row.HasBlockingIssues,
            ValidatedAtUtc = row.ValidatedAtUtc
        }, row.EditCommandId, row.CreatedBy, row.CreatedAtUtc, row.ChangeSummary);

    // Used to seed exact M3 snapshots in conformance tests. This slice exposes no mutation adapter.
    public static PlanRevisionRecord ToRecord(PlanRevisionSnapshot snapshot) => new()
    {
        RevisionId = snapshot.RevisionId,
        PlanId = snapshot.PlanId,
        ProjectId = snapshot.ProjectId,
        ParentRevisionId = snapshot.ParentRevisionId,
        VersionNumber = snapshot.VersionNumber,
        GeometrySchemaVersion = snapshot.GeometrySchemaVersion,
        GeometryJson = snapshot.GeometryJson,
        GeometryHash = snapshot.GeometryHash,
        LandRevisionId = snapshot.LandRevisionId,
        RequirementsRevisionId = snapshot.RequirementsRevisionId,
        EditCommandId = snapshot.EditCommandId,
        CreatedBy = snapshot.CreatedBy,
        CreatedAtUtc = snapshot.CreatedAtUtc,
        ChangeSummary = snapshot.ChangeSummary,
        ValidationId = snapshot.Validation.ValidationId,
        ValidatorVersion = snapshot.Validation.ValidatorVersion,
        ValidationGeometryHash = snapshot.Validation.GeometryHash,
        GeometryValid = snapshot.Validation.GeometryValid,
        RequirementsMatch = snapshot.Validation.RequirementsMatch,
        RegulatoryEvidenceStatus = snapshot.Validation.RegulatoryEvidenceStatus,
        HasBlockingIssues = snapshot.Validation.HasBlockingIssues,
        ValidatedAtUtc = snapshot.Validation.ValidatedAtUtc
    };
}
