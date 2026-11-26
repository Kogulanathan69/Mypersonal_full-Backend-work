using System.Text.Json;
using System.Text.Json.Nodes;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Diagnostics;

namespace BuildMind.Application.ContractTests.Integration;

internal static class AdvancedGeometryContractScenarios
{
    private const string FixturePath =
        "python-services/tests/m3_plan_contract/fixtures/advanced_geometry_cases.json";

    public static void Register(Action<string, Action> run)
    {
        run("advanced shared-wall fixture satisfies C# structural contract", () =>
        {
            var geometry = DeserializeBase();
            PlanEditorContractValidator.Validate(geometry);
            Check(geometry.Floors[0].Rooms.Count == 2, "Adjacent room fixture was not preserved.");
            Check(geometry.Floors[0].Openings.Single().Type == "door", "Shared-wall door was not preserved.");
        });

        run("advanced fixture duplicate identity is rejected by C#", () =>
        {
            var root = BaseNode();
            var rooms = root["floors"]![0]!["rooms"]!.AsArray();
            rooms[1]!["id"] = rooms[0]!["id"]!.GetValue<string>();
            CheckThrows<ArgumentException>(() => PlanEditorContractValidator.Validate(Deserialize(root)));
        });

        run("advanced fixture missing structural reference is rejected by C#", () =>
        {
            var root = BaseNode();
            root["floors"]![0]!["rooms"]![0]!["wallIds"]![0] = Guid.NewGuid().ToString();
            CheckThrows<ArgumentException>(() => PlanEditorContractValidator.Validate(Deserialize(root)));
        });

        run("advanced fixture invalid opening position is rejected by C#", () =>
        {
            var root = BaseNode();
            root["floors"]![0]!["openings"]![0]!["offset"] = 2.5;
            root["floors"]![0]!["openings"]![0]!["width"] = 1.0;
            CheckThrows<ArgumentException>(() => PlanEditorContractValidator.Validate(Deserialize(root)));
        });

        run("unsupported topology diagnostic remains stable and blocking", () =>
        {
            Check(PlanEditorErrorCodes.UnsupportedTopology == "UNSUPPORTED_TOPOLOGY",
                "Unsupported topology identifier changed.");
            Check(PlanEditorErrorCodes.All.Contains(PlanEditorErrorCodes.UnsupportedTopology),
                "Unsupported topology is missing from the proposed catalog.");
        });
    }

    private static PlanGeometryDto DeserializeBase() => Deserialize(BaseNode());

    private static PlanGeometryDto Deserialize(JsonNode node) =>
        JsonSerializer.Deserialize<PlanGeometryDto>(node.ToJsonString(), PlanEditorJson.StrictOptions)
        ?? throw new Exception("Advanced geometry fixture deserialized to null.");

    private static JsonNode BaseNode()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FixtureLocator.ResolveRepositoryFile(FixturePath)));
        return JsonNode.Parse(document.RootElement.GetProperty("baseSharedWallPlan").GetRawText())
            ?? throw new Exception("Advanced geometry fixture is missing baseSharedWallPlan.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void CheckThrows<T>(Action action) where T : Exception
    {
        try { action(); }
        catch (T) { return; }
        throw new Exception($"Expected {typeof(T).Name}.");
    }
}
