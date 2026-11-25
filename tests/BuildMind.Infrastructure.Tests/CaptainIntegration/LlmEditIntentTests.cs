using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Infrastructure.CaptainIntegration;

namespace BuildMind.Infrastructure.Tests.CaptainIntegration;

/// <summary>Server-side half of the LLM pipeline: schema re-validation and bounded intent → command mapping.</summary>
public sealed class LlmEditIntentTests
{
    private static readonly Guid Floor = Guid.NewGuid(), Kitchen = Guid.NewGuid(), Bed1 = Guid.NewGuid(), Bed2 = Guid.NewGuid();
    private static readonly Guid KitchenNorth = Guid.NewGuid(), Door = Guid.NewGuid();

    private static FloorDto Plan()
    {
        RoomDto Room(Guid id, string name, string type, double x0, double y0, double x1, double y1, params Guid[] walls) => new()
        {
            Id = id, Name = name, Type = type, WallIds = walls,
            Polygon = [new() { X = x0, Y = y0 }, new() { X = x1, Y = y0 }, new() { X = x1, Y = y1 }, new() { X = x0, Y = y1 }],
        };
        WallDto Wall(Guid id, double x0, double y0, double x1, double y1) =>
            new() { Id = id, Start = new() { X = x0, Y = y0 }, End = new() { X = x1, Y = y1 }, Thickness = 0.1 };
        return new FloorDto
        {
            Id = Floor, Level = 0,
            Rooms = [Room(Kitchen, "Kitchen 1", "kitchen", 0, 0, 4, 3, KitchenNorth), Room(Bed1, "Bedroom 1", "bedroom", 4, 0, 8, 3),
                     Room(Bed2, "Bedroom 2", "bedroom", 8, 0, 12, 3)],
            Walls = [Wall(KitchenNorth, 4, 3, 0, 3)],
            Openings = [new OpeningDto { Id = Door, Type = "door", WallId = KitchenNorth, Offset = 1, Width = 0.9 }],
        };
    }

    private static JsonElement Intent(string commandType, string room = "", double amount = 0, string unit = "none", string direction = "none",
        string axis = "none", string openingRef = "", string openingType = "none", string decision = "propose") =>
        JsonSerializer.SerializeToElement(new { decision, commandType, room, amount, unit, direction, axis, openingRef, openingType });

    private static PlanEditCommandDto? Map(JsonElement intent)
    {
        var floor = Plan();
        return OpenAiCompatibleEditProposalPort.ToCommand(intent, floor, [("D1", floor.Openings[0])]);
    }

    [Fact]
    public void Valid_intents_map_to_one_bounded_server_computed_command()
    {
        var resize = Map(Intent("resizeRoom", "kitchen", 10, "percent", "larger"))!;
        Assert.Equal((PlanEditCommandTypes.ResizeRoom, Kitchen), (resize.CommandType, resize.TargetId));
        Assert.Equal(0, resize.Payload.GetProperty("anchorX").GetDouble());
        var move = Map(Intent("moveRoom", "Bedroom 2", 1, "m", "east"))!;
        Assert.Equal(1, move.Payload.GetProperty("deltaX").GetDouble());
        var add = Map(Intent("addOpening", "kitchen", 0, "none", "north", openingType: "window"))!;
        Assert.Equal(KitchenNorth, add.Payload.GetProperty("opening").GetProperty("wallId").GetGuid());
        Assert.Equal(Door, Map(Intent("removeOpening", openingRef: "d1"))!.TargetId);
        Assert.Equal(1.5, Map(Intent("moveOpening", amount: 0.5, unit: "m", direction: "west", openingRef: "D1"))!.Payload.GetProperty("offset").GetDouble());
    }

    [Theory]
    [InlineData("resizeRoom", "bedroom", 10, "percent", "larger")]   // ambiguous: two bedrooms
    [InlineData("resizeRoom", "garage", 10, "percent", "larger")]    // unknown room
    [InlineData("resizeRoom", "kitchen", 90, "percent", "larger")]   // out of bounds
    [InlineData("moveRoom", "kitchen", 50, "m", "east")]             // out of bounds
    [InlineData("moveRoom", "kitchen", 1, "percent", "east")]        // wrong unit
    public void Unsafe_or_ambiguous_intents_produce_no_command(string type, string room, double amount, string unit, string direction) =>
        Assert.Null(Map(Intent(type, room, amount, unit, direction)));

    [Fact]
    public void Model_rejection_and_unknown_opening_refs_produce_no_command()
    {
        Assert.Null(Map(Intent("resizeRoom", "kitchen", 10, "percent", "larger", decision: "reject")));
        Assert.Null(Map(Intent("removeOpening", openingRef: "W9")));
        Assert.Null(Map(Intent("none")));
    }

    [Theory]
    [InlineData("""{"decision":"propose"}""")]                                                          // missing fields
    [InlineData("""{"decision":"propose","commandType":"dropTable","room":"","amount":0,"unit":"none","direction":"none","axis":"none","openingRef":"","openingType":"none"}""")]
    [InlineData("""{"decision":"propose","commandType":"moveRoom","room":"x","amount":"1","unit":"m","direction":"east","axis":"none","openingRef":"","openingType":"none"}""")]
    [InlineData("""{"decision":"propose","commandType":"moveRoom","room":"x","amount":1,"unit":"m","direction":"east","axis":"none","openingRef":"","openingType":"none","sql":"x"}""")]
    public void Schema_violations_are_rejected_server_side(string json) =>
        Assert.Throws<JsonException>(() => OpenAiCompatibleEditProposalPort.ValidateSchema(JsonDocument.Parse(json).RootElement));

    private static PlanEditCommandDto Target(Guid room) => new()
    {
        CommandId = Guid.NewGuid(), CommandType = "resizeRoom", FloorId = Floor, TargetId = room,
        Payload = System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone(),
    };

    [Theory]
    [InlineData("Make the bedroom bigger", true)]           // two bedrooms: the model picked one arbitrarily
    [InlineData("make bedroom 1 bigger", false)]
    [InlineData("Please enlarge Bedroom 1 by 10%", false)]
    [InlineData("make bed 1 larger", false)]
    [InlineData("make bedroom 2 bigger", true)]             // names the OTHER bedroom than the targeted one
    public void Ambiguous_room_reference_is_detected_deterministically(string instruction, bool ambiguous) =>
        Assert.Equal(ambiguous, OpenAiCompatibleEditProposalPort.AmbiguousRoomReference(Target(Bed1), Plan(), instruction));

    [Fact]
    public void A_unique_room_type_is_never_ambiguous() =>
        Assert.False(OpenAiCompatibleEditProposalPort.AmbiguousRoomReference(Target(Kitchen), Plan(), "make it bigger"));
}
