using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Confirmation;
using BuildMind.Application.Features.PlanEditor.Versioning;
using BuildMind.Application.Features.Reports.Contracts;
using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;

namespace BuildMind.Reports.Tests.Fixtures;

// Synthetic structural fixtures: not authoritative geometry, regulatory or consent evidence.
internal static class SyntheticReportFixtures
{
    public static readonly DateTimeOffset Time = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);
    public static readonly Guid Project = Id(1), Requirement = Id(2), Plan = Id(3), Revision = Id(4), Land = Id(5), User = Id(6), Validation = Id(7), Confirmation = Id(8), ReportId = Id(9), ArtifactId = Id(10);
    public static Guid Id(int n) => new(n, 0, 0, new byte[8]);
    public static RequirementRevision Requirements() => new(Requirement, Project, 1, "test-schema-v1", "test-catalogue-v1", new RequirementContent(1, [new("ROOM_A", 1)], [], [], null), Time);
    public static ReportDisclaimer Disclaimer() => new("test-v1", "Synthetic preliminary report disclaimer.");
    public static string Geometry() => JsonSerializer.Serialize(new PlanGeometryDto {
        SchemaVersion = GeometrySchemaInfo.DraftVersion, ProjectId = Project, PlanId = Plan,
        LandRevisionId = Land, RequirementsRevisionId = Requirement,
        CoordinateSystem = new() { Type = "local", Unit = "m", Origin = new() { X = 0, Y = 0 }, XDirection = "right", YDirection = "up" },
        Floors = [new FloorDto { Id = Id(11), Level = 0, Rooms = [], Walls = [], Openings = [] }]
    });
    public static PlanRevisionSnapshot PlanSource(string? json = null, bool? requirementsMatch = true, string evidence = "VERIFIED")
    {
        json ??= Geometry();
        var hash = PlanRevisionService.ComputeGeometryHash(json);
        return new(Revision, Plan, Project, null, 1, GeometrySchemaInfo.DraftVersion, json, hash, Land, Requirement,
            new PlanValidationReferenceDto { ValidationId = Validation, ValidatorVersion = "test-validator", GeometryHash = hash,
                GeometryValid = true, RequirementsMatch = requirementsMatch, RegulatoryEvidenceStatus = evidence,
                HasBlockingIssues = false, ValidatedAtUtc = Time }, null, User, Time, null);
    }
    public static PlanConfirmationSnapshot Confirm(PlanRevisionSnapshot plan) => new(Confirmation, Project, Plan, Revision, plan.GeometryHash, Validation, User, Time.AddMinutes(1), true);
    public static ReportSnapshot Snapshot() { var plan = PlanSource(); return ReportInputSnapshot.FromResolvedSources("test-report-v1", Time.AddMinutes(2), Requirements(), plan, Confirm(plan), Disclaimer()).Snapshot; }
    public static Report Pending() => Report.CreatePending(ReportId, Project, User, Time.AddMinutes(2), Snapshot());
    public static ReportArtifact Artifact() => new(ArtifactId, "application/pdf", 128, "test-content-hash", Time.AddMinutes(3));
}
