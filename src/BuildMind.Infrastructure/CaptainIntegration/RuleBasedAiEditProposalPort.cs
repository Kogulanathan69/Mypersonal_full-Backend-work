using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;

namespace BuildMind.Infrastructure.CaptainIntegration;

/// <summary>
/// RULE-BASED LOCAL proposal adapter for natural-language plan edits. This is NOT an LLM and must never be presented
/// as one (the API labels results <c>RULE_BASED_LOCAL</c>). It parses a small deterministic English grammar into
/// exactly one bounded structured command; the existing M3 orchestration then previews it in Python, validates it on
/// the server and stores a candidate that the customer must explicitly apply. Nothing leaves the host and nothing
/// from the instruction text is copied into the command except bounded numbers.
/// Grammar: make|enlarge|shrink the &lt;room&gt; larger|bigger|smaller [by N%] | wider|narrower|longer|shorter [by N m]
///          | move the &lt;room&gt; north|south|east|west|up|down|left|right by N m
/// </summary>
internal sealed partial class RuleBasedAiEditProposalPort(IPlanRevisionStore revisions) : IAiEditProposalPort
{
    public const string Source = "RULE_BASED_LOCAL";
    private const double MinMetres = 0.1, MaxMetres = 5, MinPercent = 1, MaxPercent = 50;

    [GeneratedRegex(@"^(?:please\s+)?(?:make|enlarge|shrink|resize)\s+(?:the\s+)?(?<room>[a-z0-9 ]{2,40}?)\s+(?<dir>larger|bigger|smaller|wider|narrower|longer|shorter)(?:\s+by\s+(?<n>\d+(?:\.\d+)?)\s*(?<unit>%|percent|m|metres|meters))?\.?$")]
    private static partial Regex Resize();

    [GeneratedRegex(@"^(?:please\s+)?move\s+(?:the\s+)?(?<room>[a-z0-9 ]{2,40}?)\s+(?<dir>north|south|east|west|up|down|left|right)\s+by\s+(?<n>\d+(?:\.\d+)?)\s*(?:m|metres|meters)\.?$")]
    private static partial Regex Move();

    public async Task<AiEditProposal> ProposeAsync(Guid projectId, Guid planId, Guid baseRevisionId, string customerInstruction, string correlationId,
        CancellationToken cancellationToken)
    {
        var text = Regex.Replace((customerInstruction ?? "").Trim().ToLowerInvariant(), @"\s+", " ");
        if (text.Length is 0 or > 300) return Reject("INSTRUCTION_NOT_SUPPORTED", "Instruction is empty or too long.");
        var head = await revisions.GetCurrentAsync(projectId, planId, cancellationToken).ConfigureAwait(false);
        if (head is null || head.RevisionId != baseRevisionId) return Reject("STALE_BASE_REVISION", "The plan has changed; reload it first.");
        var geometry = JsonSerializer.Deserialize<PlanGeometryDto>(head.GeometryJson, PlanEditorJson.StrictOptions);
        if (geometry is null) return Reject("PLAN_UNREADABLE", "The plan could not be read.");

        var resize = Resize().Match(text);
        var move = resize.Success ? Match.Empty : Move().Match(text);
        if (!resize.Success && !move.Success)
            return Reject("INSTRUCTION_NOT_SUPPORTED",
                "Supported: 'make the kitchen larger by 10%', 'make the living room wider by 0.5 m', 'move the bathroom east by 1 m'.");
        var match = resize.Success ? resize : move;
        var target = FindRoom(geometry, match.Groups["room"].Value.Trim());
        if (target.Error is { } error) return Reject(error, error == "AMBIGUOUS_TARGET"
            ? "More than one room matches; use the room's name (for example 'bedroom 2')." : "No room with that name or type exists.");
        var (floor, room) = (target.Floor!, target.Room!);
        var xs = room.Polygon.Select(p => p.X).ToArray();
        var ys = room.Polygon.Select(p => p.Y).ToArray();
        double width = xs.Max() - xs.Min(), depth = ys.Max() - ys.Min();
        var n = match.Groups["n"].Success ? double.Parse(match.Groups["n"].Value, CultureInfo.InvariantCulture) : (double?)null;
        var dir = match.Groups["dir"].Value;

        object payload;
        string type;
        if (move.Success)
        {
            if (n is not { } metres || metres is < MinMetres or > MaxMetres) return Reject("AMOUNT_OUT_OF_RANGE", "Moves must be 0.1–5 m.");
            var (dx, dy) = dir switch { "east" or "right" => (metres, 0.0), "west" or "left" => (-metres, 0.0), "north" or "up" => (0.0, metres), _ => (0.0, -metres) };
            (type, payload) = (PlanEditCommandTypes.MoveRoom, new { deltaX = dx, deltaY = dy });
        }
        else
        {
            var unit = match.Groups["unit"].Value;
            var grow = dir is "larger" or "bigger" or "wider" or "longer";
            double scaleX, scaleY;
            if (dir is "larger" or "bigger" or "smaller")
            {
                if (unit is "m" or "metres" or "meters") return Reject("INSTRUCTION_NOT_SUPPORTED", "Use a percentage for larger/smaller.");
                var percent = n ?? 10;
                if (percent is < MinPercent or > MaxPercent) return Reject("AMOUNT_OUT_OF_RANGE", "Size changes must be 1–50%.");
                var f = Math.Sqrt(1 + (grow ? percent : -percent) / 100);
                (scaleX, scaleY) = (f, f);
            }
            else
            {
                double? metres = unit is "%" or "percent" ? null : n ?? 0.5;
                if (metres is not { } m || m is < MinMetres or > MaxMetres) return Reject("AMOUNT_OUT_OF_RANGE", "Width/length changes must be 0.1–5 m.");
                var along = dir is "wider" or "narrower" ? width : depth;
                var scale = (along + (grow ? m : -m)) / along;
                if (scale <= 0.2) return Reject("AMOUNT_OUT_OF_RANGE", "The room would become too small.");
                if (dir is "wider" or "narrower") (scaleX, scaleY) = (scale, 1.0);
                else (scaleX, scaleY) = (1.0, scale);
            }
            (type, payload) = (PlanEditCommandTypes.ResizeRoom,
                new { scaleX = Math.Round(scaleX, 6), scaleY = Math.Round(scaleY, 6), anchorX = xs.Min(), anchorY = ys.Min() });
        }
        var command = new PlanEditCommandDto
        {
            CommandId = Guid.NewGuid(), CommandType = type, FloorId = floor.Id, TargetId = room.Id,
            Payload = JsonSerializer.SerializeToElement(payload),
        };
        return new AiEditProposal(AiEditProposalStatus.Proposed, [command],
            [new PlanEditDiagnosticDto { Code = Source, Message = "Proposed by the local rule-based parser (not an AI model). Review before applying.", Severity = "INFO" }]);
    }

    private static (FloorDto? Floor, RoomDto? Room, string? Error) FindRoom(PlanGeometryDto geometry, string phrase)
    {
        var rooms = geometry.Floors.SelectMany(f => f.Rooms.Select(r => (Floor: f, Room: r))).ToArray();
        static string Norm(string? s) => Regex.Replace((s ?? "").ToLowerInvariant(), "[^a-z0-9]", "");
        var key = Norm(phrase);
        var byName = rooms.Where(x => Norm(x.Room.Name) == key).ToArray();
        if (byName.Length == 1) return (byName[0].Floor, byName[0].Room, null);
        var byType = rooms.Where(x => Norm(x.Room.Type) == key || Norm(x.Room.Type) == key.TrimEnd('s')).ToArray();
        return byType.Length switch
        {
            1 => (byType[0].Floor, byType[0].Room, null),
            0 => (null, null, "TARGET_NOT_FOUND"),
            _ => (null, null, "AMBIGUOUS_TARGET"),
        };
    }

    private static AiEditProposal Reject(string code, string message) =>
        new(AiEditProposalStatus.Rejected, [], [new PlanEditDiagnosticDto { Code = code, Message = message, Severity = "ERROR" }]);
}
