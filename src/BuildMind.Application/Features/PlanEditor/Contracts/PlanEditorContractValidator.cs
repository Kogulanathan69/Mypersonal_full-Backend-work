using System.Text.Json;

namespace BuildMind.Application.Features.PlanEditor.Contracts;

/// <summary>
/// Structural validation for the isolated draft contract. This does not
/// replace M6 authoritative geometry or regulatory validation.
/// </summary>
public static class PlanEditorContractValidator
{
    private static readonly HashSet<string> SupportedCommands =
    [
        PlanEditCommandTypes.AddRoom,
        PlanEditCommandTypes.RemoveRoom,
        PlanEditCommandTypes.MoveRoom,
        PlanEditCommandTypes.ResizeRoom,
        PlanEditCommandTypes.UpdateWall,
        PlanEditCommandTypes.AddOpening,
        PlanEditCommandTypes.MoveOpening,
        PlanEditCommandTypes.RemoveOpening
    ];

    private static readonly HashSet<string> SupportedStatuses =
    [
        PlanEditStatus.Accepted,
        PlanEditStatus.AcceptedWithWarnings,
        PlanEditStatus.Rejected,
        PlanEditStatus.RevisionConflict,
        PlanEditStatus.ValidationUnavailable
    ];

    public static void Validate(PlanGeometryDto geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        RequireDraftVersion(geometry.SchemaVersion);
        RequireNonEmpty(geometry.ProjectId, nameof(geometry.ProjectId));
        RequireNonEmpty(geometry.PlanId, nameof(geometry.PlanId));
        RequireNonEmpty(geometry.LandRevisionId, nameof(geometry.LandRevisionId));
        RequireNonEmpty(geometry.RequirementsRevisionId, nameof(geometry.RequirementsRevisionId));
        ArgumentNullException.ThrowIfNull(geometry.CoordinateSystem);
        ArgumentNullException.ThrowIfNull(geometry.Floors);

        if (geometry.Floors.Count is < 1 or > 100)
            throw new ArgumentException("A plan must contain between one and one hundred floors.");

        if (geometry.CoordinateSystem.Type != "localCartesian" || geometry.CoordinateSystem.Unit != "m")
            throw new ArgumentException("Only the provisional localCartesian metre convention is supported.");
        if (geometry.CoordinateSystem.XDirection != "right" || geometry.CoordinateSystem.YDirection != "up")
            throw new ArgumentException("Unsupported coordinate axis direction.");
        RequireFinite(geometry.CoordinateSystem.Origin);

        var ids = new HashSet<Guid>();
        foreach (var floor in geometry.Floors)
        {
            ArgumentNullException.ThrowIfNull(floor);
            ArgumentNullException.ThrowIfNull(floor.Rooms);
            ArgumentNullException.ThrowIfNull(floor.Walls);
            ArgumentNullException.ThrowIfNull(floor.Openings);
            AddUnique(ids, floor.Id);
            if (floor.Rooms.Count > 10_000 || floor.Walls.Count > 20_000 || floor.Openings.Count > 20_000)
                throw new ArgumentException("Floor entity count exceeds the draft contract limit.");
            if (floor.Walls.Any(wall => wall is null))
                throw new ArgumentException("Wall collections cannot contain null entries.");
            var walls = floor.Walls.ToDictionary(wall => wall.Id);
            foreach (var wall in floor.Walls)
            {
                AddUnique(ids, wall.Id);
                RequireFinite(wall.Start);
                RequireFinite(wall.End);
                if (!double.IsFinite(wall.Thickness) || wall.Thickness <= 0)
                    throw new ArgumentException("Wall thickness must be a positive finite value.");
                if (wall.Start.X == wall.End.X && wall.Start.Y == wall.End.Y)
                    throw new ArgumentException("A wall cannot be degenerate.");
            }

            foreach (var room in floor.Rooms)
            {
                ArgumentNullException.ThrowIfNull(room);
                ArgumentNullException.ThrowIfNull(room.Polygon);
                ArgumentNullException.ThrowIfNull(room.WallIds);
                if (room.Polygon.Any(point => point is null))
                    throw new ArgumentException("Room polygons cannot contain null points.");
                AddUnique(ids, room.Id);
                if (string.IsNullOrWhiteSpace(room.Name) || room.Name.Length > 200
                    || string.IsNullOrWhiteSpace(room.Type) || room.Type.Length > 100)
                    throw new ArgumentException("Room name and type are required and bounded.");
                if (room.Polygon.Count < 4 || room.Polygon[0].X != room.Polygon[^1].X || room.Polygon[0].Y != room.Polygon[^1].Y)
                    throw new ArgumentException("Room polygons must be closed rings with at least four points.");
                if (room.Polygon.Count > 10_000 || room.WallIds.Count is < 3 or > 10_000)
                    throw new ArgumentException("Room geometry exceeds the draft contract limits.");
                foreach (var point in room.Polygon) RequireFinite(point);
                if (room.WallIds.Any(wallId => !walls.ContainsKey(wallId)))
                    throw new ArgumentException("A room references a missing wall.");
            }

            foreach (var opening in floor.Openings)
            {
                ArgumentNullException.ThrowIfNull(opening);
                AddUnique(ids, opening.Id);
                if (!walls.TryGetValue(opening.WallId, out var wall))
                    throw new ArgumentException("An opening references a missing wall.");
                if (opening.Type is not ("door" or "window"))
                    throw new ArgumentException("Opening type must be door or window.");
                if (!double.IsFinite(opening.Offset) || opening.Offset < 0 || !double.IsFinite(opening.Width) || opening.Width <= 0)
                    throw new ArgumentException("Opening offset and width are invalid.");
                var length = Math.Sqrt(Math.Pow(wall.End.X - wall.Start.X, 2) + Math.Pow(wall.End.Y - wall.Start.Y, 2));
                if (opening.Offset + opening.Width > length)
                    throw new ArgumentException("An opening does not fit its parent wall.");
            }
        }
    }

    public static void Validate(PlanEditRequestDto request)
    {
        ArgumentNullException.ThrowIfNull(request);
        RequireDraftVersion(request.SchemaVersion);
        RequireNonEmpty(request.ProjectId, nameof(request.ProjectId));
        RequireNonEmpty(request.PlanId, nameof(request.PlanId));
        RequireNonEmpty(request.BaseRevisionId, nameof(request.BaseRevisionId));
        ArgumentNullException.ThrowIfNull(request.Command);
        RequireNonEmpty(request.Command.CommandId, nameof(request.Command.CommandId));
        RequireNonEmpty(request.Command.FloorId, nameof(request.Command.FloorId));
        if (!SupportedCommands.Contains(request.Command.CommandType))
            throw new ArgumentException("Unsupported edit command type.");
        if (request.Command.CommandType is not (PlanEditCommandTypes.AddRoom or PlanEditCommandTypes.AddOpening)
            && request.Command.TargetId is null)
            throw new ArgumentException("The edit command requires targetId.");
        PlanEditPayloadValidator.Validate(request.Command);
    }

    public static void Validate(PlanEditResultDto result)
    {
        ArgumentNullException.ThrowIfNull(result);
        RequireDraftVersion(result.SchemaVersion);
        if (!SupportedStatuses.Contains(result.Status))
            throw new ArgumentException("Unsupported edit result status.");
        if (result.Status is PlanEditStatus.Accepted or PlanEditStatus.AcceptedWithWarnings)
        {
            if (result.CandidateGeometry is null)
                throw new ArgumentException("Accepted results require candidate geometry.");
            Validate(result.CandidateGeometry);
        }
    }

    private static void RequireDraftVersion(string version)
    {
        if (version != GeometrySchemaInfo.DraftVersion)
            throw new ArgumentException($"Unsupported schema version: {version}");
    }

    private static void RequireFinite(CoordinatePointDto point)
    {
        ArgumentNullException.ThrowIfNull(point);
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new ArgumentException("Coordinates must be finite.");
    }

    private static void AddUnique(HashSet<Guid> ids, Guid id)
    {
        RequireNonEmpty(id, "entityId");
        if (!ids.Add(id)) throw new ArgumentException($"Duplicate entity ID: {id}");
    }

    private static void RequireNonEmpty(Guid id, string name)
    {
        if (id == Guid.Empty) throw new ArgumentException($"{name} cannot be empty.");
    }
}
