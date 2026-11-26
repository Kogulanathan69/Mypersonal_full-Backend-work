using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Contracts;

namespace BuildMind.Application.Features.PlanEditor.Integration;

/// <summary>Exact trusted scope shared by every BuildMind F02 mutation fingerprint.</summary>
public sealed record MutationFingerprintScope(
    Guid ProjectId,
    Guid PlanId,
    Guid OperationId,
    Guid AuthenticatedActorId,
    AuthorizationVersion AuthorizationVersion);

public sealed record CandidateCreateFingerprintInput(
    MutationFingerprintScope Scope,
    Guid ExpectedBaseRevisionId,
    Guid LandRevisionId,
    Guid RequirementsRevisionId,
    string CommandSchemaVersion,
    PlanEditCommandDto Command);

public sealed record CandidateApplyFingerprintInput(
    MutationFingerprintScope Scope,
    Guid CandidateId,
    Guid ExpectedBaseRevisionId,
    Guid LandRevisionId,
    Guid RequirementsRevisionId,
    string CandidateGeometryHash,
    Guid ValidationEvidenceId);

public sealed record CandidateRejectFingerprintInput(
    MutationFingerprintScope Scope,
    Guid CandidateId,
    string? RejectionReasonCode = null);

public sealed record CandidateExpireFingerprintInput(
    MutationFingerprintScope Scope,
    Guid CandidateId,
    DateTimeOffset ExpiresAtUtc);

public sealed record PlanRevisionAppendFingerprintInput(
    MutationFingerprintScope Scope,
    Guid ExpectedBaseRevisionId,
    string GeometryHash,
    Guid LandRevisionId,
    Guid RequirementsRevisionId,
    Guid ValidationEvidenceId,
    string GeometrySchemaVersion);

public sealed record FinalPlanConfirmationFingerprintInput(
    MutationFingerprintScope Scope,
    Guid ExactRevisionId,
    string GeometryHash,
    Guid ValidationEvidenceId,
    Guid LandRevisionId,
    Guid RequirementsRevisionId,
    bool WarningAcknowledged);

/// <summary>
/// Canonical, database-independent byte profile for F02 mutation identity. This is deliberately distinct from
/// BM-JCS-1 regulatory-evidence JSON: the domain header, typed binary fields and operation codes prevent digest
/// reuse across those security domains. Durable uniqueness and retry policy remain persistence responsibilities.
/// </summary>
public static class MutationFingerprintProfileV1
{
    public const string Name = "BuildMind-F02-Mutation-v1";
    public const ushort ProfileNumber = 1;

    private static readonly byte[] DomainHeader =
        Encoding.ASCII.GetBytes("BuildMind.F02.MutationFingerprint\0");
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    private const ushort ProjectTag = 0x0010;
    private const ushort PlanTag = 0x0011;
    private const ushort OperationIdTag = 0x0012;
    private const ushort ActorTag = 0x0013;
    private const ushort AuthorizationVersionTag = 0x0014;
    private const ushort OperationTypeTag = 0x0015;

    public static CommandFingerprint Compute(CandidateCreateFingerprintInput input) =>
        CommandFingerprint.Compute(GetCanonicalBytes(input));

    public static CommandFingerprint Compute(CandidateApplyFingerprintInput input) =>
        CommandFingerprint.Compute(GetCanonicalBytes(input));

    public static CommandFingerprint Compute(CandidateRejectFingerprintInput input) =>
        CommandFingerprint.Compute(GetCanonicalBytes(input));

    public static CommandFingerprint Compute(CandidateExpireFingerprintInput input) =>
        CommandFingerprint.Compute(GetCanonicalBytes(input));

    public static CommandFingerprint Compute(PlanRevisionAppendFingerprintInput input) =>
        CommandFingerprint.Compute(GetCanonicalBytes(input));

    public static CommandFingerprint Compute(FinalPlanConfirmationFingerprintInput input) =>
        CommandFingerprint.Compute(GetCanonicalBytes(input));

    public static byte[] GetCanonicalBytes(CandidateCreateFingerprintInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var scope = RequireScope(input.Scope);
        RequireId(input.ExpectedBaseRevisionId, nameof(input.ExpectedBaseRevisionId));
        RequireId(input.LandRevisionId, nameof(input.LandRevisionId));
        RequireId(input.RequirementsRevisionId, nameof(input.RequirementsRevisionId));
        ArgumentNullException.ThrowIfNull(input.Command);

        ValidatePayloadStructure(input.Command.Payload, 0);

        PlanEditorContractValidator.Validate(new PlanEditRequestDto
        {
            SchemaVersion = input.CommandSchemaVersion,
            ProjectId = scope.ProjectId,
            PlanId = scope.PlanId,
            BaseRevisionId = input.ExpectedBaseRevisionId,
            Command = input.Command,
        });

        var writer = Common(scope, Operation.CandidateCreate);
        writer.Required(0x0100, Id(input.ExpectedBaseRevisionId));
        writer.Required(0x0101, Id(input.LandRevisionId));
        writer.Required(0x0102, Id(input.RequirementsRevisionId));
        writer.Required(0x0103, Text(input.CommandSchemaVersion));
        writer.Required(0x0104, Id(input.Command.CommandId));
        writer.Required(0x0105, [CommandCode(input.Command.CommandType)]);
        writer.Required(0x0106, Id(input.Command.FloorId));
        writer.Optional(0x0107, input.Command.TargetId is { } target ? Id(RequireId(target, "TargetId")) : null);
        writer.Required(0x0108, Payload(input.Command));
        return Finish(Operation.CandidateCreate, writer);
    }

    public static byte[] GetCanonicalBytes(CandidateApplyFingerprintInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var writer = Common(RequireScope(input.Scope), Operation.CandidateApply);
        writer.Required(0x0200, Id(RequireId(input.CandidateId, nameof(input.CandidateId))));
        writer.Required(0x0201, [1]); // Pending
        writer.Required(0x0202, Id(RequireId(input.ExpectedBaseRevisionId, nameof(input.ExpectedBaseRevisionId))));
        writer.Required(0x0203, Id(RequireId(input.LandRevisionId, nameof(input.LandRevisionId))));
        writer.Required(0x0204, Id(RequireId(input.RequirementsRevisionId, nameof(input.RequirementsRevisionId))));
        writer.Required(0x0205, HashBytes(input.CandidateGeometryHash, nameof(input.CandidateGeometryHash)));
        writer.Required(0x0206, Id(RequireId(input.ValidationEvidenceId, nameof(input.ValidationEvidenceId))));
        return Finish(Operation.CandidateApply, writer);
    }

    public static byte[] GetCanonicalBytes(CandidateRejectFingerprintInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var writer = Common(RequireScope(input.Scope), Operation.CandidateReject);
        writer.Required(0x0300, Id(RequireId(input.CandidateId, nameof(input.CandidateId))));
        writer.Required(0x0301, [1]); // Pending
        writer.Optional(0x0302, input.RejectionReasonCode is null ? null : Text(input.RejectionReasonCode));
        return Finish(Operation.CandidateReject, writer);
    }

    public static byte[] GetCanonicalBytes(CandidateExpireFingerprintInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var writer = Common(RequireScope(input.Scope), Operation.CandidateExpire);
        writer.Required(0x0400, Id(RequireId(input.CandidateId, nameof(input.CandidateId))));
        writer.Required(0x0401, [1]); // Pending
        writer.Required(0x0402, Int64(UnixMicroseconds(input.ExpiresAtUtc)));
        return Finish(Operation.CandidateExpire, writer);
    }

    public static byte[] GetCanonicalBytes(PlanRevisionAppendFingerprintInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var writer = Common(RequireScope(input.Scope), Operation.PlanRevisionAppend);
        writer.Required(0x0500, Id(RequireId(input.ExpectedBaseRevisionId, nameof(input.ExpectedBaseRevisionId))));
        writer.Required(0x0501, HashBytes(input.GeometryHash, nameof(input.GeometryHash)));
        writer.Required(0x0502, Id(RequireId(input.LandRevisionId, nameof(input.LandRevisionId))));
        writer.Required(0x0503, Id(RequireId(input.RequirementsRevisionId, nameof(input.RequirementsRevisionId))));
        writer.Required(0x0504, Id(RequireId(input.ValidationEvidenceId, nameof(input.ValidationEvidenceId))));
        writer.Required(0x0505, Text(RequireText(input.GeometrySchemaVersion, nameof(input.GeometrySchemaVersion))));
        return Finish(Operation.PlanRevisionAppend, writer);
    }

    public static byte[] GetCanonicalBytes(FinalPlanConfirmationFingerprintInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var writer = Common(RequireScope(input.Scope), Operation.FinalPlanConfirmation);
        writer.Required(0x0600, Id(RequireId(input.ExactRevisionId, nameof(input.ExactRevisionId))));
        writer.Required(0x0601, HashBytes(input.GeometryHash, nameof(input.GeometryHash)));
        writer.Required(0x0602, Id(RequireId(input.ValidationEvidenceId, nameof(input.ValidationEvidenceId))));
        writer.Required(0x0603, Id(RequireId(input.LandRevisionId, nameof(input.LandRevisionId))));
        writer.Required(0x0604, Id(RequireId(input.RequirementsRevisionId, nameof(input.RequirementsRevisionId))));
        writer.Required(0x0605, [input.WarningAcknowledged ? (byte)1 : (byte)0]);
        return Finish(Operation.FinalPlanConfirmation, writer);
    }

    /// <summary>
    /// Checks outer profile framing and selected fixed-field invariants.
    /// This method does not fully validate nested binary payload semantics
    /// and must not be used to authorize or accept untrusted raw mutations.
    /// Typed encoders are the only supported mutation fingerprint input.
    /// </summary>
    public static bool IsCanonicalEncoding(ReadOnlySpan<byte> value)
    {
        try
        {
            if (value.Length < DomainHeader.Length + 3 || !value[..DomainHeader.Length].SequenceEqual(DomainHeader))
                return false;
            var offset = DomainHeader.Length;
            if (ReadUInt16(value, ref offset) != ProfileNumber)
                return false;
            var operation = (Operation)ReadByte(value, ref offset);
            if (!Enum.IsDefined(operation))
                return false;

            ushort previous = 0;
            var present = new HashSet<ushort>();
            var seen = new HashSet<ushort>();
            byte commandCode = 0;
            while (offset < value.Length)
            {
                var tag = ReadUInt16(value, ref offset);
                if (tag <= previous || !KnownTag(operation, tag))
                    return false;
                previous = tag;

                if (!seen.Add(tag))
                    return false;
                var presence = ReadByte(value, ref offset);
                var length = ReadUInt32(value, ref offset);
                if (presence > 1 || presence == 0 && length != 0 || length > int.MaxValue || length > value.Length - offset)
                    return false;
                var field = value.Slice(offset, checked((int)length));
                offset += checked((int)length);
                if (presence == 1)
                {
                    present.Add(tag);
                    if (tag == 0x0105 && field.Length == 1)
                        commandCode = field[0];

                    if (tag == 0x0108 &&
                        (field.IsEmpty || field[0] != commandCode))
                        return false;

                    if (!ValidFixedField(operation, tag, field))
                        return false;
                }
            }

            if (!RequiredTags(operation).All(present.Contains))
                return false;

            if (operation == Operation.CandidateCreate)
            {
                if (!seen.Contains(0x0107))
                    return false;

                // AddRoom and AddOpening may omit targetId.
                // All other supported edit commands require it.
                if (commandCode is not (1 or 6) &&
                    !present.Contains(0x0107))
                    return false;
            }

            if (operation == Operation.CandidateReject &&
                !seen.Contains(0x0302))
                return false;

            return true;
        }
        catch (Exception exception) when (exception is ArgumentException or OverflowException or InvalidOperationException)
        {
            return false;
        }
    }

    private static FieldWriter Common(MutationFingerprintScope scope, Operation operation)
    {
        var writer = new FieldWriter();
        writer.Required(ProjectTag, Id(scope.ProjectId));
        writer.Required(PlanTag, Id(scope.PlanId));
        writer.Required(OperationIdTag, Id(scope.OperationId));
        writer.Required(ActorTag, Id(scope.AuthenticatedActorId));
        writer.Required(AuthorizationVersionTag, Int64(scope.AuthorizationVersion.Value));
        writer.Required(OperationTypeTag, [(byte)operation]);
        return writer;
    }

    private static byte[] Finish(Operation operation, FieldWriter fields)
    {
        var body = fields.ToArray();
        var result = new byte[checked(DomainHeader.Length + 3 + body.Length)];
        DomainHeader.CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16BigEndian(result.AsSpan(DomainHeader.Length, 2), ProfileNumber);
        result[DomainHeader.Length + 2] = (byte)operation;
        body.CopyTo(result, DomainHeader.Length + 3);
        return result;
    }

    private static MutationFingerprintScope RequireScope(MutationFingerprintScope? scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        RequireId(scope.ProjectId, nameof(scope.ProjectId));
        RequireId(scope.PlanId, nameof(scope.PlanId));
        RequireId(scope.OperationId, nameof(scope.OperationId));
        RequireId(scope.AuthenticatedActorId, nameof(scope.AuthenticatedActorId));
        if (!scope.AuthorizationVersion.IsValid)
            throw new ArgumentException("A positive authorization version is required.", nameof(scope));
        return scope;
    }

    private static void ValidatePayloadStructure(
        JsonElement element, int depth)
    {
        if (depth != 0)
            throw new ArgumentException("Invalid payload root depth.");

        ValidateObjectShape(element, "payload");
    }

    private static void ValidateObjectShape(
        JsonElement element, string shape)
    {
        if (element.ValueKind != JsonValueKind.Object)
            throw new ArgumentException("Expected a JSON object.");

        string[] allowed = shape switch
        {
            "payload" =>
            [
                "room", "walls", "openings",
                "removeOrphanOpenings",
                "deltaX", "deltaY",
                "scaleX", "scaleY", "anchorX", "anchorY",
                "startX", "startY", "endX", "endY",
                "thickness", "opening", "offset"
            ],
            "room" => ["id", "name", "type", "polygon", "wallIds"],
            "wall" => ["id", "start", "end", "thickness"],
            "opening" => ["id", "type", "wallId", "offset", "width"],
            "point" => ["x", "y"],
            _ => throw new ArgumentException("Unsupported object shape.")
        };

        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            var name = property.Name;

            ValidateUnicode(name);

            if (!allowed.Contains(name, StringComparer.Ordinal))
                throw new ArgumentException("Unexpected payload property.");

            if (!seen.Add(name))
                throw new ArgumentException("Duplicate JSON property.");

            if (shape == "payload" && seen.Count > 5)
                throw new ArgumentException(
                    "Payload exceeds command property limits.");

            switch ((shape, name))
            {
                case ("payload", "room"):
                    ValidateObjectShape(property.Value, "room");
                    break;

                case ("payload", "walls"):
                    ValidateArrayShape(property.Value, "wall");
                    break;

                case ("payload", "openings"):
                    ValidateArrayShape(property.Value, "opening");
                    break;

                case ("payload", "opening"):
                    ValidateObjectShape(property.Value, "opening");
                    break;

                case ("room", "polygon"):
                    ValidateArrayShape(property.Value, "point");
                    break;

                case ("room", "wallIds"):
                    ValidateArrayShape(property.Value, "uuid");
                    break;

                case ("wall", "start"):
                case ("wall", "end"):
                    ValidateObjectShape(property.Value, "point");
                    break;

                default:
                    ValidateScalarShape(property.Value);
                    break;
            }
        }
    }

    private static void ValidateArrayShape(
        JsonElement element, string itemShape)
    {
        const int maximumArrayItems = 10_000;

        if (element.ValueKind != JsonValueKind.Array)
            throw new ArgumentException("Expected a JSON array.");

        if (element.GetArrayLength() > maximumArrayItems)
            throw new ArgumentException("Payload array exceeds contract limits.");

        foreach (var item in element.EnumerateArray())
        {
            if (itemShape == "uuid")
            {
                if (item.ValueKind != JsonValueKind.String ||
                    !item.TryGetGuid(out var id) ||
                    id == Guid.Empty)
                {
                    throw new ArgumentException("Invalid array identifier.");
                }
            }
            else
            {
                ValidateObjectShape(item, itemShape);
            }
        }
    }

    private static void ValidateScalarShape(JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                ValidateUnicode(element.GetString()!);
                break;

            case JsonValueKind.Number:
            case JsonValueKind.True:
            case JsonValueKind.False:
            case JsonValueKind.Null:
                break;

            default:
                throw new ArgumentException(
                    "Unexpected nested payload structure.");
        }
    }

    private static byte[] Payload(PlanEditCommandDto command)
    {
        // Payload already validated by PlanEditorContractValidator.
        var code = CommandCode(command.CommandType);
        var writer = new FieldWriter();
        switch (code)
        {
            case 1:
            {
                var room = Deserialize<RoomDto>(Required(command.Payload, "room"));
                var walls = Deserialize<List<WallDto>>(Required(command.Payload, "walls"));
                writer.Required(1, Room(room));
                writer.Required(2, List(walls.Select(Wall)));
                writer.Optional(3, command.Payload.TryGetProperty("openings", out var openings)
                    ? List(Deserialize<List<OpeningDto>>(openings).Select(Opening)) : null);
                break;
            }
            case 2:
                writer.Optional(1, command.Payload.TryGetProperty("removeOrphanOpenings", out var remove)
                    ? Bool(remove.GetBoolean()) : null);
                break;
            case 3:
                writer.Required(1, Number(Required(command.Payload, "deltaX").GetDouble()));
                writer.Required(2, Number(Required(command.Payload, "deltaY").GetDouble()));
                break;
            case 4:
                writer.Required(1, Number(Required(command.Payload, "scaleX").GetDouble()));
                writer.Required(2, Number(Required(command.Payload, "scaleY").GetDouble()));
                writer.Optional(3, OptionalNumber(command.Payload, "anchorX"));
                writer.Optional(4, OptionalNumber(command.Payload, "anchorY"));
                break;
            case 5:
                writer.Optional(1, OptionalNumber(command.Payload, "startX"));
                writer.Optional(2, OptionalNumber(command.Payload, "startY"));
                writer.Optional(3, OptionalNumber(command.Payload, "endX"));
                writer.Optional(4, OptionalNumber(command.Payload, "endY"));
                writer.Optional(5, OptionalNumber(command.Payload, "thickness"));
                break;
            case 6:
                writer.Required(1, Opening(Deserialize<OpeningDto>(Required(command.Payload, "opening"))));
                break;
            case 7:
                writer.Required(1, Number(Required(command.Payload, "offset").GetDouble()));
                break;
            case 8:
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(command));
        }

        var fields = writer.ToArray();
        var result = new byte[checked(fields.Length + 1)];
        result[0] = code;
        fields.CopyTo(result, 1);
        return result;
    }

    private static byte[] Room(RoomDto room)
    {
        var writer = new FieldWriter();
        writer.Required(1, Id(room.Id));
        writer.Required(2, Text(room.Name));
        writer.Required(3, Text(room.Type));
        writer.Required(4, List(room.Polygon.Select(Point)));
        writer.Required(5, List(room.WallIds.Select(Id)));
        return writer.ToArray();
    }

    private static byte[] Wall(WallDto wall)
    {
        var writer = new FieldWriter();
        writer.Required(1, Id(wall.Id));
        writer.Required(2, Point(wall.Start));
        writer.Required(3, Point(wall.End));
        writer.Required(4, Number(wall.Thickness));
        return writer.ToArray();
    }

    private static byte[] Opening(OpeningDto opening)
    {
        var writer = new FieldWriter();
        writer.Required(1, Id(opening.Id));
        writer.Required(2, Text(opening.Type));
        writer.Required(3, Id(opening.WallId));
        writer.Required(4, Number(opening.Offset));
        writer.Required(5, Number(opening.Width));
        return writer.ToArray();
    }

    private static byte[] Point(CoordinatePointDto point)
    {
        var writer = new FieldWriter();
        writer.Required(1, Number(point.X));
        writer.Required(2, Number(point.Y));
        return writer.ToArray();
    }

    private static byte[] List(IEnumerable<byte[]> values)
    {
        var items = values.ToArray();
        using var stream = new MemoryStream();
        WriteUInt32(stream, checked((uint)items.Length));
        foreach (var item in items)
        {
            WriteUInt32(stream, checked((uint)item.Length));
            stream.Write(item);
        }
        return stream.ToArray();
    }

    private static byte[]? OptionalNumber(JsonElement payload, string name) =>
        payload.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null
            ? Number(value.GetDouble()) : null;

    private static JsonElement Required(JsonElement value, string name) =>
        value.TryGetProperty(name, out var property)
            ? property
            : throw new ArgumentException($"Required payload property is missing: {name}");

    private static T Deserialize<T>(JsonElement element) =>
        JsonSerializer.Deserialize<T>(element.GetRawText(), PlanEditorJson.StrictOptions)
        ?? throw new ArgumentException($"{typeof(T).Name} cannot be null.");

    private static byte CommandCode(string value) => value switch
    {
        PlanEditCommandTypes.AddRoom => 1,
        PlanEditCommandTypes.RemoveRoom => 2,
        PlanEditCommandTypes.MoveRoom => 3,
        PlanEditCommandTypes.ResizeRoom => 4,
        PlanEditCommandTypes.UpdateWall => 5,
        PlanEditCommandTypes.AddOpening => 6,
        PlanEditCommandTypes.MoveOpening => 7,
        PlanEditCommandTypes.RemoveOpening => 8,
        _ => throw new ArgumentException("Unsupported edit command type.", nameof(value)),
    };

    private static byte[] Id(Guid value)
    {
        RequireId(value, nameof(value));
        var bytes = new byte[16];
        if (!value.TryWriteBytes(bytes, bigEndian: true, out var written) || written != bytes.Length)
            throw new InvalidOperationException("The UUID could not be encoded.");
        return bytes;
    }

    private static Guid RequireId(Guid value, string name)
    {
        if (value == Guid.Empty)
            throw new ArgumentException("A nonempty UUID is required.", name);
        return value;
    }

    private static string RequireText(string? value, string name)
    {
        if (string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("A nonempty value is required.", name);
        return value;
    }

    private static byte[] Text(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateUnicode(value);
        return StrictUtf8.GetBytes(value.Normalize(NormalizationForm.FormC));
    }

    private static void ValidateUnicode(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]))
            {
                if (i + 1 >= value.Length || !char.IsLowSurrogate(value[++i]))
                    throw new ArgumentException("Text contains an invalid Unicode surrogate sequence.", nameof(value));
            }
            else if (char.IsLowSurrogate(value[i]))
            {
                throw new ArgumentException("Text contains an invalid Unicode surrogate sequence.", nameof(value));
            }
        }
    }

    private static byte[] HashBytes(string? value, string name)
    {
        const string prefix = "sha256:";

        if (value is null ||
            value.Length != 71 ||
            !value.StartsWith(prefix, StringComparison.Ordinal) ||
            value.AsSpan(prefix.Length).IndexOfAnyExcept(
                "0123456789abcdef") >= 0)
        {
            throw new ArgumentException(
                "A canonical lowercase SHA-256 digest is required.", name);
        }

        return Convert.FromHexString(value.AsSpan(prefix.Length));
    }

    private static byte[] Number(double value)
    {
        if (!double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), "A finite binary64 value is required.");
        if (value == 0d)
            value = 0d;
        return Int64(BitConverter.DoubleToInt64Bits(value));
    }

    private static byte[] Bool(bool value) => [value ? (byte)1 : (byte)0];

    private static byte[] Int64(long value)
    {
        var bytes = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, value);
        return bytes;
    }

    private static long UnixMicroseconds(DateTimeOffset value)
    {
        var utcTicks = value.UtcDateTime.Ticks;
        if (utcTicks % 10 != 0)
            throw new ArgumentException("Timestamps must be normalized to whole UTC microseconds.", nameof(value));
        return checked((utcTicks - DateTime.UnixEpoch.Ticks) / 10);
    }

    private static bool KnownTag(Operation operation, ushort tag) =>
        tag is >= ProjectTag and <= OperationTypeTag || operation switch
        {
            Operation.CandidateCreate => tag is >= 0x0100 and <= 0x0108,
            Operation.CandidateApply => tag is >= 0x0200 and <= 0x0206,
            Operation.CandidateReject => tag is >= 0x0300 and <= 0x0302,
            Operation.CandidateExpire => tag is >= 0x0400 and <= 0x0402,
            Operation.PlanRevisionAppend => tag is >= 0x0500 and <= 0x0505,
            Operation.FinalPlanConfirmation => tag is >= 0x0600 and <= 0x0605,
            _ => false,
        };

    private static IEnumerable<ushort> RequiredTags(Operation operation)
    {
        yield return ProjectTag;
        yield return PlanTag;
        yield return OperationIdTag;
        yield return ActorTag;
        yield return AuthorizationVersionTag;
        yield return OperationTypeTag;
        var specific = operation switch
        {
            Operation.CandidateCreate => Enumerable.Range(0x0100, 9).Select(i => (ushort)i).Where(i => i != 0x0107),
            Operation.CandidateApply => Enumerable.Range(0x0200, 7).Select(i => (ushort)i),
            Operation.CandidateReject => [(ushort)0x0300, (ushort)0x0301],
            Operation.CandidateExpire => [(ushort)0x0400, (ushort)0x0401, (ushort)0x0402],
            Operation.PlanRevisionAppend => Enumerable.Range(0x0500, 6).Select(i => (ushort)i),
            Operation.FinalPlanConfirmation => Enumerable.Range(0x0600, 6).Select(i => (ushort)i),
            _ => throw new ArgumentOutOfRangeException(nameof(operation)),
        };
        foreach (var tag in specific)
            yield return tag;
    }

    private static bool ValidCanonicalText(
        ReadOnlySpan<byte> value, ushort tag)
    {
        var text = StrictUtf8.GetString(value);

        if (!text.IsNormalized(NormalizationForm.FormC))
            return false;

        if (tag == 0x0103)
            return string.Equals(
                text,
                GeometrySchemaInfo.DraftVersion,
                StringComparison.Ordinal);

        if (tag == 0x0505)
            return !string.IsNullOrWhiteSpace(text);

        return true;
    }

    private static bool ValidFixedField(Operation operation, ushort tag, ReadOnlySpan<byte> value)
    {
        if (tag is ProjectTag or PlanTag or OperationIdTag or ActorTag ||
            tag is 0x0100 or 0x0101 or 0x0102 or 0x0104 or 0x0106 or 0x0107 or
            0x0200 or 0x0202 or 0x0203 or 0x0204 or 0x0206 or 0x0300 or 0x0400 or
            0x0500 or 0x0502 or 0x0503 or 0x0504 or 0x0600 or 0x0602 or 0x0603 or 0x0604)
            return value.Length == 16 && !value.SequenceEqual(new byte[16]);
        if (tag is 0x0205 or 0x0501 or 0x0601)
            return value.Length == 32;
        if (tag is 0x0103 or 0x0302 or 0x0505)
            return ValidCanonicalText(value, tag);
        if (tag == AuthorizationVersionTag)
            return value.Length == 8 && BinaryPrimitives.ReadInt64BigEndian(value) > 0;
        if (tag == OperationTypeTag)
            return value.Length == 1 && value[0] == (byte)operation;
        if (tag is 0x0105)
            return value.Length == 1 && value[0] is >= 1 and <= 8;
        if (tag is 0x0201 or 0x0301 or 0x0401)
            return value.Length == 1 && value[0] == 1;
        if (tag == 0x0605)
            return value.Length == 1 && value[0] <= 1;
        if (tag == 0x0402)
            return value.Length == 8;
        return true;
    }

    private static byte ReadByte(ReadOnlySpan<byte> value, ref int offset)
    {
        if (offset >= value.Length)
            throw new ArgumentException("Truncated canonical input.");
        return value[offset++];
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> value, ref int offset)
    {
        if (value.Length - offset < 2)
            throw new ArgumentException("Truncated canonical input.");
        var result = BinaryPrimitives.ReadUInt16BigEndian(value[offset..]);
        offset += 2;
        return result;
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> value, ref int offset)
    {
        if (value.Length - offset < 4)
            throw new ArgumentException("Truncated canonical input.");
        var result = BinaryPrimitives.ReadUInt32BigEndian(value[offset..]);
        offset += 4;
        return result;
    }

    private static void WriteUInt32(Stream stream, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        stream.Write(bytes);
    }

    private enum Operation : byte
    {
        CandidateCreate = 1,
        CandidateApply = 2,
        CandidateReject = 3,
        CandidateExpire = 4,
        PlanRevisionAppend = 5,
        FinalPlanConfirmation = 6,
    }

    private sealed class FieldWriter
    {
        private readonly MemoryStream stream = new();
        private ushort previous;

        public void Required(ushort tag, byte[] value)
        {
            ArgumentNullException.ThrowIfNull(value);
            Write(tag, true, value);
        }

        public void Optional(ushort tag, byte[]? value) => Write(tag, value is not null, value ?? []);

        public byte[] ToArray() => stream.ToArray();

        private void Write(ushort tag, bool present, byte[] value)
        {
            if (tag == 0 || tag <= previous)
                throw new ArgumentException("Canonical field tags must be unique and strictly increasing.", nameof(tag));
            previous = tag;
            Span<byte> header = stackalloc byte[7];
            BinaryPrimitives.WriteUInt16BigEndian(header, tag);
            header[2] = present ? (byte)1 : (byte)0;
            BinaryPrimitives.WriteUInt32BigEndian(header[3..], checked((uint)value.Length));
            stream.Write(header);
            stream.Write(value);
        }
    }
}
