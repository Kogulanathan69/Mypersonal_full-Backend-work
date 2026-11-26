using System.Text.Json;

namespace BuildMind.Application.Features.PlanEditor.Contracts;

/// <summary>
/// Strict validation for the eight provisional command payload shapes.
/// This validates contract structure only, not authoritative geometry.
/// </summary>
public static class PlanEditPayloadValidator
{
    public static void Validate(PlanEditCommandDto command)
    {
        if (command.Payload.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Edit command payload must be a JSON object.");

        switch (command.CommandType)
        {
            case PlanEditCommandTypes.AddRoom: ValidateAddRoom(command.Payload); break;
            case PlanEditCommandTypes.RemoveRoom: ValidateRemoveRoom(command.Payload); break;
            case PlanEditCommandTypes.MoveRoom: ValidateMoveRoom(command.Payload); break;
            case PlanEditCommandTypes.ResizeRoom: ValidateResizeRoom(command.Payload); break;
            case PlanEditCommandTypes.UpdateWall: ValidateUpdateWall(command.Payload); break;
            case PlanEditCommandTypes.AddOpening: ValidateAddOpening(command.Payload); break;
            case PlanEditCommandTypes.MoveOpening: ValidateMoveOpening(command.Payload); break;
            case PlanEditCommandTypes.RemoveOpening: RequireProperties(command.Payload, [], []); break;
            default: throw new ArgumentException("Unsupported edit command type.");
        }
    }

    private static void ValidateAddRoom(JsonElement payload)
    {
        RequireProperties(payload, ["room", "walls", "openings"], ["room", "walls"]);
        var room = Deserialize<RoomDto>(Required(payload, "room"));
        ValidateRoom(room);
        var walls = DeserializeArray<WallDto>(Required(payload, "walls"), 3, 10_000, "walls");
        foreach (var wall in walls) ValidateWall(wall);
        if (payload.TryGetProperty("openings", out var openingElement))
        {
            var openings = DeserializeArray<OpeningDto>(openingElement, 0, 10_000, "openings");
            foreach (var opening in openings) ValidateOpening(opening);
        }
    }

    private static void ValidateRemoveRoom(JsonElement payload)
    {
        RequireProperties(payload, ["removeOrphanOpenings"], []);
        if (payload.TryGetProperty("removeOrphanOpenings", out var value) && value.ValueKind is not JsonValueKind.True and not JsonValueKind.False)
            throw new ArgumentException("removeOrphanOpenings must be boolean.");
    }

    private static void ValidateMoveRoom(JsonElement payload)
    {
        RequireProperties(payload, ["deltaX", "deltaY"], ["deltaX", "deltaY"]);
        FiniteNumber(Required(payload, "deltaX"), "deltaX");
        FiniteNumber(Required(payload, "deltaY"), "deltaY");
    }

    private static void ValidateResizeRoom(JsonElement payload)
    {
        RequireProperties(payload, ["scaleX", "scaleY", "anchorX", "anchorY"], ["scaleX", "scaleY"]);
        PositiveNumber(Required(payload, "scaleX"), "scaleX");
        PositiveNumber(Required(payload, "scaleY"), "scaleY");
        OptionalFiniteNumber(payload, "anchorX");
        OptionalFiniteNumber(payload, "anchorY");
    }

    private static void ValidateUpdateWall(JsonElement payload)
    {
        string[] names = ["startX", "startY", "endX", "endY", "thickness"];
        RequireProperties(payload, names, []);
        var hasValue = false;
        foreach (var name in names)
        {
            if (!payload.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null) continue;
            hasValue = true;
            if (name == "thickness") PositiveNumber(value, name); else FiniteNumber(value, name);
        }
        if (!hasValue) throw new ArgumentException("At least one wall property must be supplied.");
    }

    private static void ValidateAddOpening(JsonElement payload)
    {
        RequireProperties(payload, ["opening"], ["opening"]);
        ValidateOpening(Deserialize<OpeningDto>(Required(payload, "opening")));
    }

    private static void ValidateMoveOpening(JsonElement payload)
    {
        RequireProperties(payload, ["offset"], ["offset"]);
        NonNegativeNumber(Required(payload, "offset"), "offset");
    }

    private static void ValidateRoom(RoomDto room)
    {
        NonEmpty(room.Id, "room.id");
        if (string.IsNullOrWhiteSpace(room.Name) || room.Name.Length > 200
            || string.IsNullOrWhiteSpace(room.Type) || room.Type.Length > 100)
            throw new ArgumentException("Room name and type are required and bounded.");
        if (room.Polygon.Count is < 4 or > 10_000)
            throw new ArgumentException("Room polygon size is invalid.");
        if (room.WallIds.Count is < 3 or > 10_000)
            throw new ArgumentException("Room wallIds size is invalid.");
        foreach (var point in room.Polygon) ValidatePoint(point);
        if (room.Polygon[0].X != room.Polygon[^1].X || room.Polygon[0].Y != room.Polygon[^1].Y)
            throw new ArgumentException("Room polygon must be a closed ring.");
        foreach (var wallId in room.WallIds) NonEmpty(wallId, "room.wallIds");
    }

    private static void ValidateWall(WallDto wall)
    {
        NonEmpty(wall.Id, "wall.id");
        ValidatePoint(wall.Start);
        ValidatePoint(wall.End);
        if (!double.IsFinite(wall.Thickness) || wall.Thickness <= 0)
            throw new ArgumentException("Wall thickness must be positive and finite.");
        if (wall.Start.X == wall.End.X && wall.Start.Y == wall.End.Y)
            throw new ArgumentException("Wall cannot be degenerate.");
    }

    private static void ValidateOpening(OpeningDto opening)
    {
        NonEmpty(opening.Id, "opening.id");
        NonEmpty(opening.WallId, "opening.wallId");
        if (opening.Type is not ("door" or "window"))
            throw new ArgumentException("Opening type must be door or window.");
        if (!double.IsFinite(opening.Offset) || opening.Offset < 0)
            throw new ArgumentException("Opening offset must be nonnegative and finite.");
        if (!double.IsFinite(opening.Width) || opening.Width <= 0)
            throw new ArgumentException("Opening width must be positive and finite.");
    }

    private static void ValidatePoint(CoordinatePointDto point)
    {
        if (!double.IsFinite(point.X) || !double.IsFinite(point.Y))
            throw new ArgumentException("Coordinates must be finite.");
    }

    private static T Deserialize<T>(JsonElement element) =>
        JsonSerializer.Deserialize<T>(element.GetRawText(), PlanEditorJson.StrictOptions)
        ?? throw new ArgumentException($"{typeof(T).Name} cannot be null.");

    private static IReadOnlyList<T> DeserializeArray<T>(JsonElement element, int minimum, int maximum, string name)
    {
        if (element.ValueKind != JsonValueKind.Array)
            throw new ArgumentException($"{name} must be an array.");
        var values = Deserialize<List<T>>(element);
        if (values.Count < minimum || values.Count > maximum)
            throw new ArgumentException($"{name} item count is invalid.");
        return values;
    }

    private static void RequireProperties(JsonElement value, IReadOnlyCollection<string> allowed, IReadOnlyCollection<string> required)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in value.EnumerateObject())
        {
            if (!seen.Add(property.Name)) throw new ArgumentException($"Duplicate payload property: {property.Name}");
            if (!allowed.Contains(property.Name)) throw new ArgumentException($"Unexpected payload property: {property.Name}");
        }
        foreach (var name in required)
            if (!seen.Contains(name)) throw new ArgumentException($"Required payload property is missing: {name}");
    }

    private static JsonElement Required(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property)
            ? property
            : throw new ArgumentException($"Required payload property is missing: {name}");

    private static void OptionalFiniteNumber(JsonElement payload, string name)
    {
        if (payload.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null)
            FiniteNumber(value, name);
    }

    private static double FiniteNumber(JsonElement value, string name)
    {
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number))
            throw new ArgumentException($"{name} must be a finite number.");
        return number;
    }

    private static void PositiveNumber(JsonElement value, string name)
    {
        if (FiniteNumber(value, name) <= 0) throw new ArgumentException($"{name} must be positive.");
    }

    private static void NonNegativeNumber(JsonElement value, string name)
    {
        if (FiniteNumber(value, name) < 0) throw new ArgumentException($"{name} cannot be negative.");
    }

    private static void NonEmpty(Guid value, string name)
    {
        if (value == Guid.Empty) throw new ArgumentException($"{name} cannot be empty.");
    }
}
