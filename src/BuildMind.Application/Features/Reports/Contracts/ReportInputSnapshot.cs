using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Confirmation;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;

namespace BuildMind.Application.Features.Reports.Contracts;

/// <summary>
/// Server-side mapping of resolved source records, NOT a customer request or response DTO.
/// The caller must establish ownership, persisted confirmation and source eligibility through trusted
/// adapters. Consistent records and a matching hash alone do not establish any of those guarantees.
/// No supported-schema, freshness, regulatory or report-eligibility policy is defined here.
/// </summary>
public sealed class ReportInputSnapshot
{
    public ReportSnapshot Snapshot { get; }

    private ReportInputSnapshot(ReportSnapshot snapshot) => Snapshot = snapshot;

    public static ReportInputSnapshot FromResolvedSources(string snapshotSchemaVersion, DateTimeOffset capturedAtUtc,
        RequirementRevision requirements, PlanRevisionSnapshot plan, PlanConfirmationSnapshot confirmation,
        ReportDisclaimer disclaimer)
    {
        ArgumentNullException.ThrowIfNull(requirements);
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(confirmation);
        ArgumentNullException.ThrowIfNull(plan.Validation);
        ArgumentException.ThrowIfNullOrWhiteSpace(plan.GeometryJson);
        ArgumentException.ThrowIfNullOrWhiteSpace(plan.GeometrySchemaVersion);

        if (requirements.ProjectId != plan.ProjectId || confirmation.ProjectId != plan.ProjectId
            || confirmation.PlanId != plan.PlanId || confirmation.ConfirmedRevisionId != plan.RevisionId
            || requirements.Id != plan.RequirementsRevisionId || confirmation.ValidationId != plan.Validation.ValidationId
            || !string.Equals(confirmation.GeometryHash, plan.GeometryHash, StringComparison.Ordinal)
            || !string.Equals(plan.Validation.GeometryHash, plan.GeometryHash, StringComparison.Ordinal))
            throw new ArgumentException("Resolved report sources disagree on identity, revision or validation evidence.");

        if (!string.Equals(PlanRevisionService.ComputeGeometryHash(plan.GeometryJson), plan.GeometryHash, StringComparison.Ordinal))
            throw new ArgumentException("Stored geometry bytes do not match the M3 geometry hash.", nameof(plan));

        VerifyGeometryEnvelope(plan);

        // Copy facts from M3 DTOs into sealed immutable Domain values. Do not reinterpret unknown
        // evidence, require RequirementsMatch == true, or invent warning/eligibility policies.
        var validation = new ReportValidationSnapshot(plan.Validation.ValidationId, plan.Validation.ValidatorVersion,
            plan.Validation.GeometryHash, plan.Validation.GeometryValid, plan.Validation.RequirementsMatch,
            plan.Validation.RegulatoryEvidenceStatus, plan.Validation.HasBlockingIssues, plan.Validation.ValidatedAtUtc);
        var planSnapshot = new ReportPlanSnapshot(plan.ProjectId, plan.PlanId, plan.RevisionId, plan.VersionNumber,
            plan.GeometrySchemaVersion, plan.GeometryJson, plan.GeometryHash, plan.LandRevisionId,
            plan.RequirementsRevisionId, plan.CreatedBy, plan.CreatedAtUtc, validation);
        var confirmationSnapshot = new ReportConfirmationSnapshot(confirmation.ConfirmationId, confirmation.ProjectId,
            confirmation.PlanId, confirmation.ConfirmedRevisionId, confirmation.GeometryHash, confirmation.ValidationId,
            confirmation.ConfirmedBy, confirmation.ConfirmedAtUtc, confirmation.WarningAcknowledgement);
        return new(new ReportSnapshot(snapshotSchemaVersion, capturedAtUtc, requirements, planSnapshot,
            confirmationSnapshot, disclaimer));
    }

    private static void VerifyGeometryEnvelope(PlanRevisionSnapshot plan)
    {
        try
        {
            using var document = JsonDocument.Parse(plan.GeometryJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("Stored geometry must be a JSON object.", nameof(plan));
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
                if (!names.Add(property.Name))
                    throw new ArgumentException("Stored geometry envelope has duplicate members.", nameof(plan));

            if (!MatchesIdentity(root, "projectId", plan.ProjectId)
                || !MatchesIdentity(root, "planId", plan.PlanId)
                || !MatchesIdentity(root, "landRevisionId", plan.LandRevisionId)
                || !MatchesIdentity(root, "requirementsRevisionId", plan.RequirementsRevisionId)
                || !root.TryGetProperty("schemaVersion", out var schema) || schema.ValueKind != JsonValueKind.String
                || !string.Equals(schema.GetString(), plan.GeometrySchemaVersion, StringComparison.Ordinal))
                throw new ArgumentException("Stored geometry envelope conflicts with its revision metadata.", nameof(plan));
            // Geometry topology, coordinate/floor support and schema approval remain M3/M6 responsibilities.
        }
        catch (JsonException exception)
        {
            throw new ArgumentException("Stored geometry JSON is malformed.", nameof(plan), exception);
        }
    }

    private static bool MatchesIdentity(JsonElement root, string name, Guid expected) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && value.TryGetGuid(out var actual) && actual == expected;
}
