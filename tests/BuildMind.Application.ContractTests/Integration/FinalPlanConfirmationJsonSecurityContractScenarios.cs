using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Confirmation;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Versioning;

namespace BuildMind.Application.ContractTests.Integration;

internal static class FinalPlanConfirmationJsonSecurityContractScenarios
{
    private static readonly Guid ProjectId = Guid.Parse("10000000-0000-4000-8000-000000000001");
    private static readonly Guid PlanId = Guid.Parse("10000000-0000-4000-8000-000000000002");
    private static readonly Guid RevisionId = Guid.Parse("10000000-0000-4000-8000-000000000003");
    private static readonly Guid LandRevisionId = Guid.Parse("10000000-0000-4000-8000-000000000004");
    private static readonly Guid RequirementsRevisionId = Guid.Parse("10000000-0000-4000-8000-000000000005");
    private static readonly Guid ActorId = Guid.Parse("10000000-0000-4000-8000-000000000006");
    private static readonly Guid ValidationId = Guid.Parse("10000000-0000-4000-8000-000000000007");
    private static readonly Guid FloorId = Guid.Parse("20000000-0000-4000-8000-000000000001");
    private static readonly Guid RoomId = Guid.Parse("30000000-0000-4000-8000-000000000001");
    private static readonly Guid OpeningId = Guid.Parse("40000000-0000-4000-8000-000000000001");
    private static readonly Guid[] WallIds =
    [
        Guid.Parse("50000000-0000-4000-8000-000000000001"),
        Guid.Parse("50000000-0000-4000-8000-000000000002"),
        Guid.Parse("50000000-0000-4000-8000-000000000003"),
        Guid.Parse("50000000-0000-4000-8000-000000000004")
    ];

    public static void Register(Action<string, Action> run)
    {
        run("FC-JSON-01 duplicate top-level geometry property fails closed", () =>
            AssertRejected(DuplicateTopLevel(CanonicalJson(), "0.1-draft")));

        run("FC-JSON-02 duplicate nested room property fails closed", () =>
            AssertRejected(ReplaceOnce(
                CanonicalJson(), "\"name\":\"Bedroom\"", "\"name\":\"Bedroom\",\"name\":\"Bedroom\"")));

        run("FC-JSON-03 duplicate nested wall property fails closed", () =>
            AssertRejected(ReplaceOnce(
                CanonicalJson(), "\"thickness\":0.2", "\"thickness\":0.2,\"thickness\":0.2")));

        run("FC-JSON-04 duplicate nested opening property fails closed", () =>
            AssertRejected(ReplaceOnce(
                CanonicalJson(), "\"width\":0.9", "\"width\":0.9,\"width\":0.9")));

        run("FC-JSON-05 recomputed hash cannot legitimize different duplicate values", () =>
            AssertRejected(DuplicateTopLevel(CanonicalJson(), "unsupported-version")));

        run("FC-JSON-06 identical duplicate values remain invalid", () =>
        {
            var duplicated = DuplicateTopLevel(CanonicalJson(), GeometrySchemaInfo.DraftVersion);
            AssertThrowsJson(() => JsonSerializer.Deserialize<PlanGeometryDto>(
                duplicated, PlanEditorJson.StrictOptions));
            AssertRejected(duplicated);
        });

        run("FC-JSON-07 different duplicate values remain invalid", () =>
        {
            var duplicated = DuplicateTopLevel(CanonicalJson(), "different-value");
            AssertThrowsJson(() => JsonSerializer.Deserialize<PlanGeometryDto>(
                duplicated, PlanEditorJson.StrictOptions));
            AssertRejected(duplicated);
        });

        run("FC-JSON-08 valid canonical confirmation remains accepted", AssertValidConfirmation);
        run("FC-JSON-09 exact geometry SHA-256 compatibility is unchanged", AssertHashCompatibility);
        run("FC-JSON-10 cancellation still prevents confirmation side effects", AssertCancellation);
        run("FC-JSON-11 owner validation still prevents confirmation side effects", AssertOwnerValidation);
        run("FC-JSON-12 revision validation still prevents confirmation side effects", AssertRevisionValidation);
    }

    private static void AssertRejected(string duplicatedJson)
    {
        var recomputedHash = PlanRevisionService.ComputeGeometryHash(duplicatedJson);
        var revision = Revision(duplicatedJson, recomputedHash);
        var transactionCalls = 0;
        var publicationCalls = 0;
        var service = Service(
            revision,
            new TestProjectOwnershipGate((_, _, _) => Task.FromResult(true)),
            new TestFinalConfirmationTransaction((confirmation, _, _) =>
            {
                transactionCalls++;
                return Task.FromResult(new ConfirmationResult(ConfirmationStatus.Confirmed, confirmation));
            }),
            new TestConfirmedPlanSnapshotSink((_, _, _) =>
            {
                publicationCalls++;
                return Task.CompletedTask;
            }));

        var result = service.ConfirmAsync(
            new ConfirmPlanCommand(ProjectId, PlanId, RevisionId, recomputedHash, false),
            ActorId).GetAwaiter().GetResult();

        Check(result.Status == ConfirmationStatus.InvalidDependencyResponse,
            $"Duplicated JSON returned {result.Status} instead of failing closed.");
        Check(result.Confirmation is null, "Rejected duplicated JSON exposed a confirmation payload.");
        Check(transactionCalls == 0, "Rejected duplicated JSON reached the confirmation transaction.");
        Check(publicationCalls == 0, "Rejected duplicated JSON reached report publication.");
        Check(PlanRevisionService.ComputeGeometryHash(duplicatedJson) == recomputedHash,
            "The rejection test did not use the exact recomputed duplicate-payload hash.");
    }

    private static void AssertValidConfirmation()
    {
        var json = CanonicalJson();
        var hash = PlanRevisionService.ComputeGeometryHash(json);
        var revision = Revision(json, hash);
        var transactionCalls = 0;
        var publicationCalls = 0;
        var service = Service(
            revision,
            new TestProjectOwnershipGate((_, _, _) => Task.FromResult(true)),
            new TestFinalConfirmationTransaction((confirmation, exactRevision, _) =>
            {
                transactionCalls++;
                Check(exactRevision.GeometryJson == json, "Confirmation rewrote canonical geometry JSON.");
                Check(exactRevision.GeometryHash == hash, "Confirmation changed the exact geometry hash.");
                return Task.FromResult(new ConfirmationResult(ConfirmationStatus.Confirmed, confirmation));
            }),
            new TestConfirmedPlanSnapshotSink((_, exactRevision, _) =>
            {
                publicationCalls++;
                Check(exactRevision.GeometryJson == json, "Report publication received rewritten geometry JSON.");
                return Task.CompletedTask;
            }));

        var result = service.ConfirmAsync(
            new ConfirmPlanCommand(ProjectId, PlanId, RevisionId, hash, false),
            ActorId).GetAwaiter().GetResult();

        Check(result.Status == ConfirmationStatus.Confirmed, "Valid canonical confirmation was rejected.");
        Check(result.Confirmation?.ConfirmedRevisionId == RevisionId, "Confirmation lost the exact revision binding.");
        Check(transactionCalls == 1, "Valid confirmation did not invoke its transaction exactly once.");
        Check(publicationCalls == 1, "Valid confirmation did not publish exactly once.");
    }

    private static void AssertHashCompatibility()
    {
        var json = CanonicalJson();
        var expected = $"sha256:{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json))).ToLowerInvariant()}";
        var actual = PlanRevisionService.ComputeGeometryHash(json);

        Check(actual == expected, "Geometry hashing is no longer exact UTF-8 SHA-256.");
        Check(JsonSerializer.Serialize(
            JsonSerializer.Deserialize<PlanGeometryDto>(json, PlanEditorJson.StrictOptions),
            PlanEditorJson.StrictOptions) == json,
            "Strict parsing changed valid canonical geometry serialization.");
    }

    private static void AssertCancellation()
    {
        var json = CanonicalJson();
        var hash = PlanRevisionService.ComputeGeometryHash(json);
        var transactionCalls = 0;
        var publicationCalls = 0;
        var service = Service(
            Revision(json, hash),
            new TestProjectOwnershipGate((_, _, _) => Task.FromResult(true)),
            new TestFinalConfirmationTransaction((confirmation, _, _) =>
            {
                transactionCalls++;
                return Task.FromResult(new ConfirmationResult(ConfirmationStatus.Confirmed, confirmation));
            }),
            new TestConfirmedPlanSnapshotSink((_, _, _) =>
            {
                publicationCalls++;
                return Task.CompletedTask;
            }));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var cancelled = false;
        try
        {
            service.ConfirmAsync(
                new ConfirmPlanCommand(ProjectId, PlanId, RevisionId, hash, false),
                ActorId,
                cancellation.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            cancelled = true;
        }

        Check(cancelled, "Caller cancellation was not propagated.");
        Check(transactionCalls == 0 && publicationCalls == 0,
            "Caller cancellation reached confirmation side effects.");
    }

    private static void AssertOwnerValidation()
    {
        var json = CanonicalJson();
        var hash = PlanRevisionService.ComputeGeometryHash(json);
        var transactionCalls = 0;
        var publicationCalls = 0;
        var service = Service(
            Revision(json, hash),
            new TestProjectOwnershipGate((_, _, _) => Task.FromResult(false)),
            new TestFinalConfirmationTransaction((confirmation, _, _) =>
            {
                transactionCalls++;
                return Task.FromResult(new ConfirmationResult(ConfirmationStatus.Confirmed, confirmation));
            }),
            new TestConfirmedPlanSnapshotSink((_, _, _) =>
            {
                publicationCalls++;
                return Task.CompletedTask;
            }));

        var result = service.ConfirmAsync(
            new ConfirmPlanCommand(ProjectId, PlanId, RevisionId, hash, false),
            ActorId).GetAwaiter().GetResult();

        Check(result.Status == ConfirmationStatus.Unauthorized, "Owner denial was not preserved.");
        Check(transactionCalls == 0 && publicationCalls == 0,
            "Owner denial reached confirmation side effects.");
    }

    private static void AssertRevisionValidation()
    {
        var json = CanonicalJson();
        var hash = PlanRevisionService.ComputeGeometryHash(json);
        var transactionCalls = 0;
        var publicationCalls = 0;
        var service = Service(
            Revision(json, hash),
            new TestProjectOwnershipGate((_, _, _) => Task.FromResult(true)),
            new TestFinalConfirmationTransaction((confirmation, _, _) =>
            {
                transactionCalls++;
                return Task.FromResult(new ConfirmationResult(ConfirmationStatus.Confirmed, confirmation));
            }),
            new TestConfirmedPlanSnapshotSink((_, _, _) =>
            {
                publicationCalls++;
                return Task.CompletedTask;
            }));

        var result = service.ConfirmAsync(
            new ConfirmPlanCommand(ProjectId, PlanId, Guid.NewGuid(), hash, false),
            ActorId).GetAwaiter().GetResult();

        Check(result.Status == ConfirmationStatus.RevisionNotFound, "Exact revision validation was not preserved.");
        Check(transactionCalls == 0 && publicationCalls == 0,
            "Missing exact revision reached confirmation side effects.");
    }

    private static FinalPlanConfirmationService Service(
        PlanRevisionSnapshot revision,
        IProjectOwnershipGate ownership,
        IFinalConfirmationTransaction transaction,
        IConfirmedPlanSnapshotSink sink) => new(
            ownership,
            new TestPlanRevisionStore(revision),
            new TestApplicableInputRevisionSource((_, _) => Task.FromResult(
                new ApplicableInputRevisions(LandRevisionId, RequirementsRevisionId))),
            new TestValidationFreshnessPolicy(true),
            transaction,
            sink,
            TimeProvider.System);

    private static PlanRevisionSnapshot Revision(string json, string hash) => new(
        RevisionId,
        PlanId,
        ProjectId,
        null,
        1,
        GeometrySchemaInfo.DraftVersion,
        json,
        hash,
        LandRevisionId,
        RequirementsRevisionId,
        new PlanValidationReferenceDto
        {
            ValidationId = ValidationId,
            ValidatorVersion = "final-confirmation-json-security/1",
            GeometryHash = hash,
            GeometryValid = true,
            RequirementsMatch = true,
            RegulatoryEvidenceStatus = "VERIFIED",
            HasBlockingIssues = false,
            ValidatedAtUtc = DateTimeOffset.UtcNow
        },
        null,
        ActorId,
        DateTimeOffset.UtcNow,
        "Final confirmation JSON security fixture");

    private static string CanonicalJson() => JsonSerializer.Serialize(Geometry(), PlanEditorJson.StrictOptions);

    private static PlanGeometryDto Geometry() => new()
    {
        SchemaVersion = GeometrySchemaInfo.DraftVersion,
        ProjectId = ProjectId,
        PlanId = PlanId,
        LandRevisionId = LandRevisionId,
        RequirementsRevisionId = RequirementsRevisionId,
        CoordinateSystem = new CoordinateSystemDto
        {
            Type = "localCartesian",
            Unit = "m",
            Origin = Point(0, 0),
            XDirection = "right",
            YDirection = "up"
        },
        Floors =
        [
            new FloorDto
            {
                Id = FloorId,
                Level = 0,
                Rooms =
                [
                    new RoomDto
                    {
                        Id = RoomId,
                        Name = "Bedroom",
                        Type = "bedroom",
                        Polygon = [Point(0, 0), Point(4, 0), Point(4, 3), Point(0, 3), Point(0, 0)],
                        WallIds = WallIds
                    }
                ],
                Walls =
                [
                    Wall(WallIds[0], 0, 0, 4, 0),
                    Wall(WallIds[1], 4, 0, 4, 3),
                    Wall(WallIds[2], 4, 3, 0, 3),
                    Wall(WallIds[3], 0, 3, 0, 0)
                ],
                Openings =
                [
                    new OpeningDto
                    {
                        Id = OpeningId,
                        Type = "door",
                        WallId = WallIds[0],
                        Offset = 0.5,
                        Width = 0.9
                    }
                ]
            }
        ]
    };

    private static CoordinatePointDto Point(double x, double y) => new() { X = x, Y = y };

    private static WallDto Wall(Guid id, double startX, double startY, double endX, double endY) => new()
    {
        Id = id,
        Start = Point(startX, startY),
        End = Point(endX, endY),
        Thickness = 0.2
    };

    private static string DuplicateTopLevel(string json, string duplicateValue) => ReplaceOnce(
        json,
        $"\"schemaVersion\":\"{GeometrySchemaInfo.DraftVersion}\"",
        $"\"schemaVersion\":\"{GeometrySchemaInfo.DraftVersion}\",\"schemaVersion\":\"{duplicateValue}\"");

    private static string ReplaceOnce(string value, string oldValue, string newValue)
    {
        var index = value.IndexOf(oldValue, StringComparison.Ordinal);
        if (index < 0) throw new InvalidOperationException($"Fixture token was not found: {oldValue}");
        return string.Concat(value.AsSpan(0, index), newValue, value.AsSpan(index + oldValue.Length));
    }

    private static void AssertThrowsJson(Action action)
    {
        try
        {
            action();
        }
        catch (JsonException)
        {
            return;
        }

        throw new InvalidOperationException("Duplicated JSON was accepted by the strict parser.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
