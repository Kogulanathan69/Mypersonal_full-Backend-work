using System.Text.Json;
using System.Text.Json.Nodes;
using BuildMind.Application.Features.Generation.Contracts;

namespace BuildMind.Application.ContractTests.Integration;

// Pending approval: register this class in Program.cs with Register(Run), then
// run the existing contract test project on a supported .NET 10 SDK.
// Semantic gaps are fixture metadata and are intentionally excluded from this runner.
internal static partial class M6GenerationContractScenarios
{
    private const string FixturePath =
        "python-services/tests/m6_contract/fixtures/parity/shared-contract-cases.json";

    private static readonly IReadOnlyDictionary<string, Type> Models = new Dictionary<string, Type>
    {
        ["GenerationInput"] = typeof(GenerationInputDto),
        ["MetricLand"] = typeof(MetricLandDto),
        ["RegulatoryEvidence"] = typeof(RegulatoryEvidenceDto),
        ["FeasibilityResult"] = typeof(FeasibilityResultDto),
        ["GenerationResult"] = typeof(GenerationResultDto),
        ["ValidationResult"] = typeof(ValidationResultDto),
        ["GenerationJob"] = typeof(GenerationJobDto)
    };

    public static void Register(Action<string, Action> run)
    {
        var corpus = JsonNode.Parse(File.ReadAllText(FixtureLocator.ResolveRepositoryFile(FixturePath)))!;
        var positive = corpus["positive"]!.AsArray()
            .ToDictionary(item => item!["id"]!.GetValue<string>(), item => item!, StringComparer.Ordinal);

        foreach (var (id, fixture) in positive)
        {
            var model = Models[fixture["model"]!.GetValue<string>()];
            var payload = fixture["payload"]!;
            run($"M6 deserialization accepts {id}", () => Deserialize(payload, model));
            run($"M6 serialization preserves {id} wire fields", () => CheckSerialization(payload, model));
        }

        foreach (var entry in corpus["variants"]!.AsArray())
        {
            var fixture = entry!;
            var id = fixture["id"]!.GetValue<string>();
            var classification = fixture["csharp"]!.GetValue<string>();
            if (classification is "semantic-gap")
                continue;
            if (classification is not ("json-reject" or "known-difference"))
                throw new InvalidOperationException($"Unrecognized M6 fixture classification: {classification}");

            var basis = positive[fixture["base"]!.GetValue<string>()];
            var model = Models[basis["model"]!.GetValue<string>()];
            var category = classification == "known-difference" ? "known M3 difference" : "deserialization rejection";
            run($"M6 {category}: {id}", () =>
                ExpectRejection(() => Deserialize(ApplyChange(basis["payload"]!, fixture["change"]!), model), id));
        }
    }

    private static object Deserialize(JsonNode payload, Type model) =>
        JsonSerializer.Deserialize(payload.ToJsonString(), model, GenerationContractJson.StrictOptions)
        ?? throw new InvalidOperationException($"M6 JSON deserialized to null for {model.Name}.");

    private static void CheckSerialization(JsonNode expected, Type model)
    {
        var value = Deserialize(expected, model);
        var actual = JsonSerializer.SerializeToNode(value, model, GenerationContractJson.StrictOptions);
        CompareWire(expected, actual, "$");
    }

    private static void CompareWire(JsonNode? expected, JsonNode? actual, string path)
    {
        if (expected is JsonObject expectedObject && actual is JsonObject actualObject)
        {
            if (expectedObject.Count != actualObject.Count)
                throw new InvalidOperationException($"Property count mismatch at {path}.");
            foreach (var (name, value) in expectedObject)
            {
                if (!actualObject.TryGetPropertyValue(name, out var serialized))
                    throw new InvalidOperationException($"Missing serialized property {path}.{name}.");
                if (value is not null && name is ("createdAtUtc" or "startedAtUtc" or "completedAtUtc" or "validatedAtUtc"))
                {
                    // Explicit lexical difference: Python uses Z, while DateTimeOffset writes +00:00.
                    var timestamp = value.GetValue<string>();
                    if (!timestamp.EndsWith('Z') || serialized?.GetValue<string>() != timestamp[..^1] + "+00:00")
                        throw new InvalidOperationException($"Unexpected UTC wire spelling at {path}.{name}.");
                    continue;
                }
                CompareWire(value, serialized, $"{path}.{name}");
            }
            return;
        }
        if (expected is JsonArray expectedArray && actual is JsonArray actualArray)
        {
            if (expectedArray.Count != actualArray.Count)
                throw new InvalidOperationException($"Collection length mismatch at {path}.");
            for (var i = 0; i < expectedArray.Count; i++)
                CompareWire(expectedArray[i], actualArray[i], $"{path}[{i}]");
            return;
        }

        if (!JsonNode.DeepEquals(expected, actual))
            throw new InvalidOperationException($"Serialized value mismatch at {path}.");
    }

    private static JsonNode ApplyChange(JsonNode original, JsonNode change)
    {
        var payload = original.DeepClone();
        var path = change["path"]!.AsArray();
        var target = payload;
        for (var i = 0; i < path.Count - 1; i++)
        {
            var part = path[i]!;
            target = target is JsonArray ? target[part.GetValue<int>()]! : target[part.GetValue<string>()]!;
        }

        var leaf = path[path.Count - 1]!;
        var operation = change["op"]!.GetValue<string>();
        if (operation == "remove")
        {
            if (target is JsonArray array)
                array.RemoveAt(leaf.GetValue<int>());
            else if (!target.AsObject().Remove(leaf.GetValue<string>()))
                throw new InvalidOperationException("M6 fixture attempted to remove a missing property.");
        }
        else if (operation == "rename")
        {
            var properties = target.AsObject();
            var name = leaf.GetValue<string>();
            if (!properties.TryGetPropertyValue(name, out var value))
                throw new InvalidOperationException("M6 fixture attempted to rename a missing property.");
            properties.Remove(name);
            properties.Add(change["value"]!.GetValue<string>(), value);
        }
        else if (operation is "set" or "number")
        {
            var value = operation == "number"
                ? JsonNode.Parse(change["value"]!.GetValue<string>())
                : change["value"]?.DeepClone();
            if (operation == "number" && value?.GetValueKind() != JsonValueKind.Number)
                throw new InvalidOperationException("M6 numeric fixture requires a JSON number.");
            if (target is JsonArray array)
                array[leaf.GetValue<int>()] = value;
            else
                target[leaf.GetValue<string>()] = value;
        }
        else
        {
            throw new InvalidOperationException($"Unsupported M6 fixture operation: {operation}");
        }
        return payload;
    }

    private static void ExpectRejection(Action action, string id)
    {
        try
        {
            action();
        }
        catch (JsonException)
        {
            return;
        }
        throw new InvalidOperationException($"Expected a C# JSON rejection for {id}.");
    }
}
