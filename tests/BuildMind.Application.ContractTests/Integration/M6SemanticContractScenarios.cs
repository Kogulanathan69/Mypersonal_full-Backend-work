using System.Text.Json;
using System.Text.Json.Nodes;
using BuildMind.Application.Features.Generation.Contracts;

namespace BuildMind.Application.ContractTests.Integration;

internal static partial class M6GenerationContractScenarios
{
    public static void RegisterSemantics(Action<string, Action> run)
    {
        var corpus = JsonNode.Parse(File.ReadAllText(FixtureLocator.ResolveRepositoryFile(FixturePath)))!;
        var positive = corpus["positive"]!.AsArray()
            .ToDictionary(entry => entry!["id"]!.GetValue<string>(), entry => entry!);

        JsonObject Payload(string id) => positive[id]["payload"]!.DeepClone().AsObject();

        foreach (var (id, fixture) in positive)
        {
            var type = Models[fixture["model"]!.GetValue<string>()];
            run($"positive: {id}", () => GenerationContractBoundary.Parse(Payload(id).ToJsonString(), type));
        }
        foreach (var entry in corpus["variants"]!.AsArray())
        {
            var fixture = entry!;
            var id = fixture["id"]!.GetValue<string>();
            var basis = fixture["base"]!.GetValue<string>();
            var type = Models[positive[basis]["model"]!.GetValue<string>()];
            var classification = fixture["csharp"]!.GetValue<string>();
            run($"{classification}: {id}", () =>
            {
                var payload = ApplyChange(Payload(basis), fixture["change"]!).ToJsonString();
                if (classification == "semantic-gap")
                {
                    var dto = JsonSerializer.Deserialize(payload, type, GenerationContractJson.StrictOptions);
                    ExpectSafeRejection(() => GenerationContractValidation.Validate(dto));
                }
                ExpectSafeRejection(() => GenerationContractBoundary.Parse(payload, type));
            });
        }

        void Reject(string title, string basis, Action<JsonObject> change)
            => run(title, () =>
            {
                var payload = Payload(basis);
                change(payload);
                ExpectSafeRejection(() => GenerationContractBoundary.Parse(
                    payload.ToJsonString(), Models[positive[basis]["model"]!.GetValue<string>()]));
            });

        Reject("null coordinate element", "metric-land", payload => payload["boundary"]!["exterior"]![1] = null);
        Reject("explicit null holes", "metric-land", payload => payload["boundary"]!["holes"] = null);
        Reject("hole count limit", "metric-land", payload =>
            payload["boundary"]!["holes"] = new JsonArray(Enumerable.Range(0, 101)
                .Select(_ => payload["boundary"]!["exterior"]!.DeepClone()).ToArray()));
        Reject("fractional seed", "generation-input", payload => payload["seed"] = JsonNode.Parse("42.0"));
        Reject("duplicate room slot", "generation-input", payload =>
        {
            var rooms = payload["roomProgram"]!["rooms"]!.AsArray();
            rooms.Add(rooms[0]!.DeepClone());
        });
        Reject("self adjacency", "generation-input", payload =>
        {
            var id = payload["roomProgram"]!["rooms"]![0]!["slotId"]!;
            payload["adjacencyPreferences"] = new JsonArray(new JsonObject
            {
                ["fromSlotId"] = id.DeepClone(),
                ["toSlotId"] = id.DeepClone(),
                ["relationship"] = "adjacent"
            });
        });
        Reject("missing defined nullable area", "feasibility-needs-review", payload => payload.Remove("landAreaM2"));
        Reject("feasibility decision needs actual area", "feasibility-empty", payload => payload["buildableAreaM2"] = null);
        Reject("wrong validation candidate", "generation-structural-success", payload =>
            payload["alternatives"]![0]!["validation"]!["candidateId"] = "11111111-1111-4111-8111-111111111111");
        Reject("nil floor entity", "generation-structural-success", payload =>
            payload["alternatives"]![0]!["geometry"]!["floors"]![0]!["id"] = Guid.Empty.ToString());
        Reject("job idempotency seed mismatch", "job-running", payload => payload["idempotency"]!["seed"] = 0);
        Reject("unknown job diagnostic classification", "job-failed", payload =>
            payload["failure"]!["diagnostics"]![0]!["code"] = "CALLER_SELECTED");
        Reject("diagnostic pointer invalid escape", "job-failed", payload =>
            payload["failure"]!["diagnostics"]![0]!["path"] = "/private/~2token");

        run("optional omission defaults and maximum seed", () =>
        {
            var payload = Payload("generation-input");
            payload["land"]!["boundary"]!.AsObject().Remove("holes");
            var dimensions = payload["roomProgram"]!["rooms"]![0]!["dimensions"]!.AsObject();
            dimensions.Remove("maxWidthM");
            payload["seed"] = int.MaxValue;
            var input = GenerationContractBoundary.Parse<GenerationInputDto>(payload.ToJsonString());
            Check(input.Seed == int.MaxValue && input.Land.Boundary.Holes.Count == 0
                && input.RoomProgram.Rooms[0].Dimensions.MaxWidthM is null);
        });

        run("duplicate decoded property at nested depth", () =>
        {
            const string raw = """{"outer":[{"SECRET_TOKEN":1,"\u0053ECRET_TOKEN":2}]}""";
            ExpectSafeRejection(() => GenerationContractBoundary.Parse<GenerationJobDto>(raw), "JSON_INVALID");
        });
        run("duplicate top-level property", () =>
            ExpectSafeRejection(() => GenerationContractBoundary.Parse<GenerationJobDto>(
                """{"jobId":1,"jobId":2}"""), "JSON_INVALID"));
        run("malformed JSON produces safe error", () =>
            ExpectSafeRejection(() => GenerationContractBoundary.Parse<GenerationJobDto>(
                """{"SECRET_TOKEN":NaN}"""), "JSON_INVALID"));
        run("unpaired surrogate in value produces safe error", () =>
        {
            var payload = Payload("job-queued").ToJsonString().Replace(
                "\"QUEUED\"", "\"\\uD800\"", StringComparison.Ordinal);
            ExpectSafeRejection(() => GenerationContractBoundary.Parse<GenerationJobDto>(payload));
        });
        run("unknown key and external errors never echo submitted text", () =>
        {
            var payload = Payload("job-queued");
            payload["SECRET_TOKEN"] = "SECRET_TOKEN";
            ExpectSafeRejection(() => GenerationContractBoundary.Parse<GenerationJobDto>(payload.ToJsonString()));
            Check(!GenerationContractBoundary.FormatPublicIssue(new InvalidOperationException("SECRET_TOKEN"))
                .Message.Contains("SECRET_TOKEN", StringComparison.Ordinal));
        });

        foreach (var (wire, expected) in new[]
        {
            ("2026-11-19T12:00:00Z", "2026-11-19T12:00:00Z"),
            ("2026-11-19T12:00:00+00:00", "2026-11-19T12:00:00Z"),
            ("2026-11-19T12:00:00.1+14:01", "2026-11-18T21:59:00.100000Z"),
            ("2026-11-19T12:00:00-23:59", "2026-11-20T11:59:00Z"),
            ("2026-11-19T12:00:00+23:59", "2026-11-18T12:01:00Z")
        })
        {
            run($"timestamp acceptance: {wire}", () =>
            {
                var payload = Payload("job-queued");
                payload["createdAtUtc"] = wire;
                var job = GenerationContractBoundary.Parse<GenerationJobDto>(payload.ToJsonString());
                var published = JsonNode.Parse(GenerationContractBoundary.PublishJob(job))!;
                Check(job.CreatedAtUtc.Offset == TimeSpan.Zero && published["createdAtUtc"]!.GetValue<string>() == expected);
            });
        }
        foreach (var invalid in new[]
        {
            "2026-11-19T12:00:00+24:00", "2026-11-19T12:00:00.1234567Z", "2026-11-19T12:00:00",
            "0001-01-01T00:00:00+00:01", "9999-12-31T23:59:59-00:01", "2026-11-19t12:00:00z"
        })
            Reject($"timestamp rejection: {invalid}", "job-queued", payload => payload["createdAtUtc"] = invalid);

        GenerationJobDto Job(string id) => GenerationContractBoundary.Parse<GenerationJobDto>(Payload(id).ToJsonString());

        run("legal job transitions preserve history", () =>
        {
            var queued = Job("job-queued");
            var running = Job("job-running");
            GenerationContractBoundary.PublishJob(running, queued);
            foreach (var terminal in new[] { "job-succeeded", "job-infeasible", "job-failed" })
                GenerationContractBoundary.PublishJob(Job(terminal), running);
        });
        run("illegal transitions and terminal rewrites", () =>
        {
            ExpectSafeRejection(() => GenerationContractBoundary.PublishJob(Job("job-failed"), Job("job-queued")));
            ExpectSafeRejection(() => GenerationContractBoundary.PublishJob(Job("job-queued"), Job("job-running")));
            ExpectSafeRejection(() => GenerationContractBoundary.PublishJob(Job("job-failed"), Job("job-succeeded")));
        });
        run("job update rejects rewritten identity and start history", () =>
        {
            var payload = Payload("job-failed");
            payload["jobId"] = "11111111-1111-4111-8111-111111111111";
            var running = Job("job-running");
            ExpectSafeRejection(() => GenerationContractBoundary.PublishJob(
                GenerationContractBoundary.Parse<GenerationJobDto>(payload.ToJsonString()), running));
            payload["jobId"] = running.JobId.ToString();
            payload["startedAtUtc"] = running.StartedAtUtc!.Value.AddSeconds(1).ToString("yyyy-MM-dd'T'HH:mm:ss'Z'");
            ExpectSafeRejection(() => GenerationContractBoundary.PublishJob(
                GenerationContractBoundary.Parse<GenerationJobDto>(payload.ToJsonString()), running));
        });
        run("job failure publication replaces untrusted diagnostic fields", () =>
        {
            var payload = Payload("job-failed");
            var diagnostic = payload["failure"]!["diagnostics"]![0]!;
            diagnostic["message"] = "SECRET_TOKEN";
            diagnostic["path"] = "/SECRET_TOKEN";
            diagnostic["sourceReferenceIds"] = new JsonArray(JsonValue.Create("SECRET_TOKEN"));
            var job = GenerationContractBoundary.Parse<GenerationJobDto>(payload.ToJsonString());
            var output = GenerationContractBoundary.PublishJob(job);
            Check(!output.Contains("SECRET_TOKEN", StringComparison.Ordinal));
            var published = GenerationContractBoundary.Parse<GenerationJobDto>(output).Failure!.Diagnostics[0];
            Check(published.Message == "The generation search ended without a result."
                && published.Path is null && published.SourceReferenceIds.Count == 0);
        });
        run("job success publication curates nested and unknown diagnostics", () =>
        {
            var payload = Payload("job-succeeded");
            static JsonObject Warning() => new()
            {
                ["code"] = "SECRET_TOKEN",
                ["severity"] = "WARNING",
                ["message"] = "SECRET_TOKEN"
            };
            payload["result"]!["diagnostics"]!.AsArray().Add(Warning());
            payload["result"]!["alternatives"]![0]!["validation"]!["diagnostics"]!.AsArray().Add(Warning());
            var job = GenerationContractBoundary.Parse<GenerationJobDto>(payload.ToJsonString());
            var output = GenerationContractBoundary.PublishJob(job);
            Check(!output.Contains("SECRET_TOKEN", StringComparison.Ordinal));
            var published = GenerationContractBoundary.Parse<GenerationJobDto>(output);
            Check(published.Result!.Diagnostics[^1].Code == "UNCLASSIFIED_DIAGNOSTIC"
                && published.Result.Alternatives[0].Validation.Diagnostics[^1].Code == "UNCLASSIFIED_DIAGNOSTIC");
        });
    }

    private static void Check(bool valid)
    {
        if (!valid)
            throw new InvalidOperationException("Expected M6 contract behavior.");
    }

    private static void ExpectSafeRejection(Action action, string code = "CONTRACT_VALUE_INVALID")
    {
        try
        {
            action();
        }
        catch (GenerationContractException error)
        {
            var issue = GenerationContractBoundary.FormatPublicIssue(error);
            Check(issue.Code == code && !issue.Message.Contains("SECRET_TOKEN", StringComparison.Ordinal)
                && error.InnerException is null);
            return;
        }
        throw new InvalidOperationException("Expected an M6 boundary rejection.");
    }
}
