using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;
using BuildMind.Application.Features.PlanEditor.Contracts;

namespace BuildMind.Application.Features.Generation.Contracts;

/// <summary>Structural and cross-field checks for the provisional M6 contracts.</summary>
public static partial class GenerationContractValidation
{
    public static void Validate(object? contract)
    {
        switch (contract)
        {
            case GenerationInputDto input: Input(input); break;
            case MetricLandDto land: Land(land); break;
            case RegulatoryEvidenceDto evidence: Evidence(evidence); break;
            case FeasibilityResultDto feasibility: Feasibility(feasibility); break;
            case GenerationResultDto result: Result(result); break;
            case ValidationResultDto validation: Validation(validation); break;
            case GenerationJobDto job: Job(job); break;
            default: throw new GenerationContractException();
        }
    }

    private static void Require([DoesNotReturnIf(false)] bool valid)
    {
        if (!valid)
            throw new GenerationContractException();
    }

    private static void Id(Guid value) => Require(value != Guid.Empty);

    private static (Guid, Guid, Guid) Revision(RevisionBindingDto value)
        => (value.ProjectId, value.LandRevisionId, value.RequirementsRevisionId);

    private static void Binding(RevisionBindingDto value)
    {
        Id(value.ProjectId);
        Id(value.LandRevisionId);
        Id(value.RequirementsRevisionId);
    }

    private static void Length(string? value, int minimum, int maximum)
        => Require(value is not null && value.EnumerateRunes().Count() >= minimum
            && value.EnumerateRunes().Count() <= maximum);

    private static void Text(string? value)
    {
        Length(value, 1, 500);
        Require(!string.IsNullOrWhiteSpace(value));
    }

    private static void Pattern(string? value, string pattern)
        => Require(value is not null && Regex.IsMatch(value, pattern, RegexOptions.CultureInvariant | RegexOptions.NonBacktracking));

    private static void Items<T>([NotNull] IReadOnlyList<T>? values, int maximum, int minimum = 0)
    {
        Require(values is not null && values.Count >= minimum && values.Count <= maximum);
        Require(values.All(item => item is not null));
    }

    private static void Area(double? value) => Require(value is null || (double.IsFinite(value.Value) && value >= 0));
    private static void Positive(double value) => Require(double.IsFinite(value) && value > 0);
    private static void Hash(string? value) => Pattern(value, @"\A[0-9a-f]{64}\z");
    private static void Code(string? value) => Pattern(value, @"\A[A-Z][A-Z0-9_]{0,79}\z");
    private static void Timestamp(DateTimeOffset value) => Require(value.Ticks % 10 == 0);

    private static void Point(CoordinatePointDto? point)
        => Require(point is not null && double.IsFinite(point.X) && double.IsFinite(point.Y));

    private static void Coordinates(CoordinateSystemDto? value)
    {
        Require(value is not null && value.Type == MetricLandCoordinateValues.Type
            && value.Unit == MetricLandCoordinateValues.Unit
            && value.XDirection == MetricLandCoordinateValues.XDirection
            && value.YDirection == MetricLandCoordinateValues.YDirection);
        Point(value.Origin);
    }

    private static void Ring(IReadOnlyList<CoordinatePointDto>? ring)
    {
        Items(ring, 10_000, 4);
        foreach (var point in ring)
            Point(point);
        Require(ring[0].X == ring[^1].X && ring[0].Y == ring[^1].Y);
    }

    private static void Land(MetricLandDto? value)
    {
        Require(value is not null && value.SchemaVersion == FeasibilitySchemas.MetricLand);
        Coordinates(value.CoordinateSystem);
        Require(value.Boundary is not null);
        Ring(value.Boundary.Exterior);
        Items(value.Boundary.Holes, 100);
        foreach (var hole in value.Boundary.Holes)
            Ring(hole);
    }

    private static void EvidenceStatus(string? status, string? version)
    {
        Require(status is "MISSING" or "UNVERIFIED" or "CONFLICTING" or "REVIEWED");
        if (version is not null)
            Text(version);
        Require(status != "REVIEWED" || version is not null);
    }

    private static void Evidence(RegulatoryEvidenceDto? value)
    {
        Require(value is not null);
        EvidenceStatus(value.Status, value.RuleSetVersion);
        Items(value.Constraints, 1_000);
        Items(value.Gaps, 1_000);
        Require(value.Constraints.Count == 0 || value.RuleSetVersion is not null);
        Require(value.Status switch
        {
            "MISSING" => value.RuleSetVersion is null && value.Constraints.Count == 0 && value.Gaps.Count > 0,
            "REVIEWED" => value.Constraints.Count > 0 && value.Gaps.Count == 0,
            _ => value.Gaps.Count > 0
        });
        var identities = new HashSet<(string, int)>();
        foreach (var item in value.Constraints)
        {
            Require(item.Type == GenerationConstraintTypes.Setback && item.ParcelEdgeIndex >= 0);
            Require(identities.Add((item.RuleId, item.ParcelEdgeIndex)));
            Text(item.RuleId);
            Area(item.DistanceM);
            Id(item.ReviewedBy);
            Text(item.ReviewReference);
            Require(item.Source is not null && item.Applicability is not null);
            Text(item.Source.SourceVersion);
            Text(item.Source.SourceLocator);
            Text(item.Source.Jurisdiction);
            Text(item.Applicability.Basis);
            Require(item.Applicability.Status is "APPLICABLE" or "NOT_APPLICABLE" or "UNKNOWN");
            Require(value.Status != "REVIEWED" || item.Applicability.Status != "UNKNOWN");
        }
        foreach (var gap in value.Gaps)
            Text(gap.Code);
    }

    private static void Input(GenerationInputDto value)
    {
        Binding(value);
        Require(value.SchemaVersion == GenerationSchemas.Input && value.Seed >= 0);
        Land(value.Land);
        Evidence(value.RegulatoryEvidence);
        Require(value.RegulatoryEvidence.Constraints.All(item => item.ParcelEdgeIndex < value.Land.Boundary.Exterior.Count - 1));

        var program = value.RoomProgram;
        Require(program is not null && program.Level == 0 && program.CatalogueVersion == GenerationSchemas.RoomCatalogue);
        Items(program.Rooms, 100, 1);
        var slots = new HashSet<Guid>();
        foreach (var room in program.Rooms)
        {
            Id(room.SlotId);
            Require(slots.Add(room.SlotId) && room.RoomType is "bedroom" or "livingRoom" or "kitchen" or "bathroom");
            var dimensions = room.Dimensions;
            Require(dimensions is not null);
            Positive(dimensions.MinWidthM);
            Positive(dimensions.MinDepthM);
            Require(dimensions.MaxWidthM is null || (double.IsFinite(dimensions.MaxWidthM.Value) && dimensions.MaxWidthM >= dimensions.MinWidthM));
            Require(dimensions.MaxDepthM is null || (double.IsFinite(dimensions.MaxDepthM.Value) && dimensions.MaxDepthM >= dimensions.MinDepthM));
        }

        Items(value.AdjacencyPreferences, 4_950);
        var pairs = new HashSet<(Guid, Guid)>();
        foreach (var item in value.AdjacencyPreferences)
        {
            var from = item.FromSlotId;
            var to = item.ToSlotId;
            Require(from != to && slots.Contains(from) && slots.Contains(to));
            Require(item.Relationship is "adjacent" or "separated");
            Require(pairs.Add(from.CompareTo(to) < 0 ? (from, to) : (to, from)));
        }
    }

    private static bool Critical(IReadOnlyList<DiagnosticDto>? diagnostics)
    {
        Items(diagnostics, 1_000);
        bool critical = false;
        foreach (var item in diagnostics)
        {
            Code(item.Code);
            Require(item.Severity is "CRITICAL" or "WARNING");
            critical |= item.Severity == DiagnosticSeverities.Critical;
            Text(item.Message);
            Pattern(item.Message, @"\A[^\x00-\x1f\x7f]+\z");
            if (item.Path is not null)
            {
                Length(item.Path, 0, 500);
                Pattern(item.Path, @"\A(?:/(?:[^~/\x00-\x1f\x7f]|~[01])*)*\z");
            }
            Items(item.RelatedEntityIds, 100);
            foreach (var id in item.RelatedEntityIds)
                Id(id);
            Items(item.SourceReferenceIds, 100);
            foreach (var source in item.SourceReferenceIds)
                Text(source);
        }
        return critical;
    }

    private static void Feasibility(FeasibilityResultDto value)
    {
        Binding(value);
        Require(value.SchemaVersion == FeasibilitySchemas.Result);
        Require(value.Status is "FEASIBLE" or "INFEASIBLE" or "NEEDS_REVIEW" or "UNSUPPORTED");
        EvidenceStatus(value.RegulatoryEvidenceStatus, value.RuleSetVersion);
        Text(value.FeasibilityPolicyVersion);
        var critical = Critical(value.Diagnostics);
        var area = value.BuildableAreaM2;
        var required = value.RequiredAreaM2;
        Area(area);
        Area(required);
        Area(value.LandAreaM2);
        Require(area is null || (value.LandAreaM2 is not null && area <= value.LandAreaM2));
        Require((value.BuildableFootprint is not null) == (area > 0));
        if (value.BuildableFootprint is not null)
            Land(value.BuildableFootprint);
        if (value.Status is "FEASIBLE" or "INFEASIBLE")
        {
            Require(area is not null && required is not null);
            Require((value.Status == "FEASIBLE") == (area > 0 && area >= required));
        }
        Require(value.Status == "FEASIBLE"
            ? !critical && value.RegulatoryEvidenceStatus == "REVIEWED"
            : value.Diagnostics.Count > 0);
    }

    private static void Validation(ValidationResultDto value)
    {
        Binding(value);
        Id(value.ValidationId);
        Id(value.CandidateId);
        Hash(value.GeometryHash);
        Timestamp(value.ValidatedAtUtc);
        Text(value.ValidatorVersion);
        EvidenceStatus(value.RegulatoryEvidenceStatus, value.RuleSetVersion);
        var critical = Critical(value.Diagnostics);
        Require(value.HasBlockingIssues == critical);
        Require(critical || (value.GeometryValid == true && value.RequirementsMatch == true
            && value.RegulatoryEvidenceStatus == "REVIEWED"));
    }

    private static void Geometry(PlanGeometryDto value, RevisionBindingDto binding)
    {
        Require(value.SchemaVersion == GeometrySchemaInfo.DraftVersion);
        Require((value.ProjectId, value.LandRevisionId, value.RequirementsRevisionId) == Revision(binding));
        Coordinates(value.CoordinateSystem);
        Id(value.PlanId);
        Items(value.Floors, 100, 1);
        var entityIds = new HashSet<Guid> { value.PlanId };
        void Entity(Guid id)
        {
            Id(id);
            Require(entityIds.Add(id));
        }
        foreach (var floor in value.Floors)
        {
            Entity(floor.Id);
            Items(floor.Rooms, 10_000);
            Items(floor.Walls, 20_000);
            Items(floor.Openings, 20_000);
            var wallIds = new HashSet<Guid>();
            foreach (var wall in floor.Walls)
            {
                Entity(wall.Id);
                wallIds.Add(wall.Id);
                Point(wall.Start);
                Point(wall.End);
                Positive(wall.Thickness);
            }
            foreach (var room in floor.Rooms)
            {
                Entity(room.Id);
                Length(room.Name, 1, 200);
                Length(room.Type, 1, 100);
                Ring(room.Polygon);
                Items(room.WallIds, 10_000, 3);
                Require(room.WallIds.Distinct().Count() == room.WallIds.Count && room.WallIds.All(wallIds.Contains));
            }
            foreach (var opening in floor.Openings)
            {
                Entity(opening.Id);
                Require(opening.Type is "door" or "window");
                Require(wallIds.Contains(opening.WallId));
                Area(opening.Offset);
                Positive(opening.Width);
            }
        }
    }

    private static void Result(GenerationResultDto value)
    {
        Binding(value);
        Require(value.SchemaVersion == GenerationSchemas.Result && value.Seed >= 0);
        Require(value.Outcome is "SUCCESS" or "UNAVAILABLE" or "INFEASIBLE" or "NEEDS_REVIEW" or "UNSUPPORTED" or "FAILED");
        Items(value.Alternatives, 3);
        var critical = Critical(value.Diagnostics);
        if (value.AlgorithmVersion is not null)
            Text(value.AlgorithmVersion);
        if (value.Outcome != "SUCCESS")
        {
            Code(value.ReasonCode);
            Require(value.Alternatives.Count == 0 && critical);
            Require(value.Outcome != "UNAVAILABLE" || value.ReasonCode == "GENERATION_UNAVAILABLE");
            return;
        }

        Require(value.Alternatives.Count == 3 && value.ReasonCode is null && value.AlgorithmVersion is not null && !critical);
        foreach (var item in value.Alternatives)
        {
            Require(Revision(item) == Revision(value) && item.AlgorithmVersion == value.AlgorithmVersion);
            Id(item.AlternativeId);
            Require(item.Geometry is not null && item.Metrics is not null
                && item.Validation is not null && item.DiversitySignature is not null);
            Geometry(item.Geometry, item);
            Area(item.Metrics.FloorAreaM2);
            Area(item.Metrics.FootprintAreaM2);
            Require(item.Metrics.RoomCount is >= 0 and <= 100_000);
            Validation(item.Validation);
            Require(!item.Validation.HasBlockingIssues && Revision(item.Validation) == Revision(item)
                && item.Validation.CandidateId == item.Geometry.PlanId);
            Text(item.DiversitySignature.Version);
            Pattern(item.DiversitySignature.Value, @"\A[A-Za-z0-9._-]{1,128}\z");
        }
        Require(value.Alternatives.Select(item => item.AlternativeId).Distinct().Count() == 3);
        Require(value.Alternatives.Select(item => item.Geometry.PlanId).Distinct().Count() == 3);
        Require(value.Alternatives.Select(item => item.Validation.ValidationId).Distinct().Count() == 3);
        Require(value.Alternatives.Select(item => item.DiversitySignature.Version).Distinct().Count() == 1);
        Require(value.Alternatives.Select(item => item.DiversitySignature.Value).Distinct().Count() == 3);
        Require(value.Alternatives.Select(item => item.Validation.RuleSetVersion).Distinct().Count() == 1);
    }
}
