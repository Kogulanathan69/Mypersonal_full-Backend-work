using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Integration;

namespace BuildMind.Application.ContractTests.Integration;

internal static class MutationFingerprintContractScenarios
{
    private static Guid Id(int value) =>
        Guid.Parse($"00000000-0000-0000-0000-{value:x12}");

    private static MutationFingerprintScope Scope() =>
        new(Id(1), Id(2), Id(3), Id(4),
            new AuthorizationVersion(7));

    private const string GeometryHash =
        "sha256:000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";

    private const string Schema = "0.1-draft";

    public static void Register(Action<string, Action> run)
    {
        RegisterEditGoldenVectors(run);
        RegisterSecurityTests(run);
        RegisterBoundaryTests(run);
        RegisterStressTests(run);

        run("FP-GV-01 CandidateCreate golden vector", () =>
        {
            using var json = JsonDocument.Parse(
                """{"deltaX":1.25,"deltaY":-0.5}""");

            var input = new CandidateCreateFingerprintInput(
                Scope(), Id(5), Id(6), Id(7), Schema,
                new PlanEditCommandDto
                {
                    CommandId = Id(8),
                    CommandType = PlanEditCommandTypes.MoveRoom,
                    FloorId = Id(9),
                    TargetId = Id(10),
                    Payload = json.RootElement.Clone()
                });

            Check(
                MutationFingerprintProfileV1.GetCanonicalBytes(input),
                MutationFingerprintProfileV1.Compute(input),
                352,
                "10c7d88f58c82a272ca29c485f8ae21c27a7acc5af969554e9afddbe83aa3308");
        });

        run("FP-GV-02 CandidateApply golden vector", () =>
        {
            var input = new CandidateApplyFingerprintInput(
                Scope(), Id(11), Id(5), Id(6), Id(7),
                GeometryHash, Id(12));

            Check(
                MutationFingerprintProfileV1.GetCanonicalBytes(input),
                MutationFingerprintProfileV1.Compute(input),
                314,
                "9fbbd90ca37dfea5c067bbb79d2c1784f4c376d02c2c10a1cc3e247ccf2628cb");
        });

        run("FP-GV-03 CandidateReject golden vector", () =>
        {
            var input = new CandidateRejectFingerprintInput(
                Scope(), Id(11));

            Check(
                MutationFingerprintProfileV1.GetCanonicalBytes(input),
                MutationFingerprintProfileV1.Compute(input),
                190,
                "3a1726aec43184606e359adb36e8484b703712fe12f2581e044bd0727e58c999");
        });

        run("FP-GV-04 CandidateExpire golden vector", () =>
        {
            var input = new CandidateExpireFingerprintInput(
                Scope(), Id(11),
                DateTimeOffset.UnixEpoch.AddTicks(12_345_670));

            Check(
                MutationFingerprintProfileV1.GetCanonicalBytes(input),
                MutationFingerprintProfileV1.Compute(input),
                198,
                "014d184397c29aba09e750d832f01e97ebfd093c583641d7dac569faa49b8465");
        });

        run("FP-GV-05 PlanRevisionAppend golden vector", () =>
        {
            var input = new PlanRevisionAppendFingerprintInput(
                Scope(), Id(5), GeometryHash,
                Id(6), Id(7), Id(12), Schema);

            Check(
                MutationFingerprintProfileV1.GetCanonicalBytes(input),
                MutationFingerprintProfileV1.Compute(input),
                299,
                "4018ccb84ce903c91b364096ebab7d420966618a8f427882a939aa17412275f8");
        });

        run("FP-GV-06 FinalPlanConfirmation golden vector", () =>
        {
            var input = new FinalPlanConfirmationFingerprintInput(
                Scope(), Id(5), GeometryHash, Id(12),
                Id(6), Id(7), true);

            Check(
                MutationFingerprintProfileV1.GetCanonicalBytes(input),
                MutationFingerprintProfileV1.Compute(input),
                291,
                "5a7d36fda78ccde344921531b9eca3abd9baf767fdd4aa796fda358a9e39efbe");
        });
    }

    private static void RegisterEditGoldenVectors(
        Action<string, Action> run)
    {
        var addRoom = JsonSerializer.Serialize(new
        {
            room = new
            {
                id = Id(21),
                name = "Room",
                type = "bedroom",
                polygon = new[]
                {
                    new { x = 0, y = 0 },
                    new { x = 4, y = 0 },
                    new { x = 0, y = 3 },
                    new { x = 0, y = 0 }
                },
                wallIds = new[] { Id(22), Id(23), Id(24) }
            },
            walls = new[]
            {
                new
                {
                    id = Id(22),
                    start = new { x = 0, y = 0 },
                    end = new { x = 4, y = 0 },
                    thickness = 0.2
                },
                new
                {
                    id = Id(23),
                    start = new { x = 4, y = 0 },
                    end = new { x = 0, y = 3 },
                    thickness = 0.2
                },
                new
                {
                    id = Id(24),
                    start = new { x = 0, y = 3 },
                    end = new { x = 0, y = 0 },
                    thickness = 0.2
                }
            },
            openings = Array.Empty<object>()
        });

        var addOpening = JsonSerializer.Serialize(new
        {
            opening = new
            {
                id = Id(25),
                type = "door",
                wallId = Id(22),
                offset = 0.5,
                width = 0.9
            }
        });

        var vectors = new
            (string Name, string Command, string Payload,
             int Length, string Digest)[]
        {
            (
                "FP-GV-07 AddRoom",
                PlanEditCommandTypes.AddRoom,
                addRoom,
                949,
                "a1f23fdb5f7712d5141acc21827357f6dd13f9e855b2e42aae7b7f16c5c9d3fd"
            ),
            (
                "FP-GV-08 RemoveRoom",
                PlanEditCommandTypes.RemoveRoom,
                """{"removeOrphanOpenings":true}""",
                330,
                "3470238a61c8bfeee746ef253259d46acc756eaa13f0d6069b03721ba375d8ac"
            ),
            (
                "FP-GV-09 ResizeRoom",
                PlanEditCommandTypes.ResizeRoom,
                """{"scaleX":1.2,"scaleY":0.8,"anchorX":null,"anchorY":0}""",
                374,
                "bd26e7b3fa53977677bd7423e80740e5b1e7c4df942c966aa8ba69e99d51322d"
            ),
            (
                "FP-GV-10 UpdateWall",
                PlanEditCommandTypes.UpdateWall,
                """{"thickness":0.25}""",
                365,
                "872d37bd0ac14e85d1759924a91f0002780a78fdc48f1f1f80d71ebd40aa3484"
            ),
            (
                "FP-GV-11 AddOpening",
                PlanEditCommandTypes.AddOpening,
                addOpening,
                400,
                "da7b2a9fec72645e9a7603c54e645fcb930dccf34fef98b059e1234d43acc354"
            ),
            (
                "FP-GV-12 MoveOpening",
                PlanEditCommandTypes.MoveOpening,
                """{"offset":1.5}""",
                337,
                "6551bfcf82f99e4609da4edf362e6bcdda5f2f1fb3fdf48d11d579c4415461dc"
            ),
            (
                "FP-GV-13 RemoveOpening",
                PlanEditCommandTypes.RemoveOpening,
                "{}",
                322,
                "7eeb9d931a27cc2a16862ad57dd31ec678ea5c0b4cb9aea20bf1a1b367f4aa71"
            )
        };

        foreach (var vector in vectors)
        {
            run(vector.Name, () =>
            {
                using var json = JsonDocument.Parse(vector.Payload);

                var input = new CandidateCreateFingerprintInput(
                    Scope(), Id(5), Id(6), Id(7), Schema,
                    new PlanEditCommandDto
                    {
                        CommandId = Id(8),
                        CommandType = vector.Command,
                        FloorId = Id(9),
                        TargetId = vector.Command is
                            PlanEditCommandTypes.AddRoom or
                            PlanEditCommandTypes.AddOpening
                                ? null : Id(10),
                        Payload = json.RootElement.Clone()
                    });

                Check(
                    MutationFingerprintProfileV1.GetCanonicalBytes(input),
                    MutationFingerprintProfileV1.Compute(input),
                    vector.Length,
                    vector.Digest);
            });
        }
    }

    private static CandidateCreateFingerprintInput CreateEdit(
        string command,
        string payload,
        Guid? target)
    {
        using var json = JsonDocument.Parse(payload);

        return new CandidateCreateFingerprintInput(
            Scope(), Id(5), Id(6), Id(7), Schema,
            new PlanEditCommandDto
            {
                CommandId = Id(8),
                CommandType = command,
                FloorId = Id(9),
                TargetId = target,
                Payload = json.RootElement.Clone()
            });
    }

    private static void ExpectRejected(Action action)
    {
        try
        {
            action();
        }
        catch (ArgumentException)
        {
            return;
        }
        catch (JsonException)
        {
            return;
        }

        throw new Exception("Invalid fingerprint input was accepted.");
    }

    private static void SameFingerprint(
        CommandFingerprint first,
        CommandFingerprint second)
    {
        if (!first.Matches(second))
            throw new Exception("Equivalent inputs produced different fingerprints.");
    }

    private static void DifferentFingerprint(
        CommandFingerprint first,
        CommandFingerprint second)
    {
        if (!first.IsValid || !second.IsValid || first.Matches(second))
            throw new Exception("Distinct inputs were not separated.");
    }

    private static void RegisterSecurityTests(
        Action<string, Action> run)
    {
        var baseline = new CandidateRejectFingerprintInput(
            Scope(), Id(11));

        var move = CreateEdit(
            PlanEditCommandTypes.MoveRoom,
            """{"deltaX":1.25,"deltaY":-0.5}""",
            Id(10));

        run("FP-SEC-01 Empty actor rejected", () =>
            ExpectRejected(() => MutationFingerprintProfileV1.Compute(
                baseline with
                {
                    Scope = Scope() with
                    {
                        AuthenticatedActorId = Guid.Empty
                    }
                })));

        run("FP-SEC-02 Empty project rejected", () =>
            ExpectRejected(() => MutationFingerprintProfileV1.Compute(
                baseline with
                {
                    Scope = Scope() with { ProjectId = Guid.Empty }
                })));

        run("FP-SEC-03 Empty operation ID rejected", () =>
            ExpectRejected(() => MutationFingerprintProfileV1.Compute(
                baseline with
                {
                    Scope = Scope() with { OperationId = Guid.Empty }
                })));

        run("FP-SEC-04 Invalid authorization version rejected", () =>
            ExpectRejected(() => MutationFingerprintProfileV1.Compute(
                baseline with
                {
                    Scope = Scope() with
                    {
                        AuthorizationVersion = default
                    }
                })));

        run("FP-SEC-05 Empty candidate ID rejected", () =>
            ExpectRejected(() => MutationFingerprintProfileV1.Compute(
                baseline with { CandidateId = Guid.Empty })));

        run("FP-SEC-06 Invalid digest rejected", () =>
            ExpectRejected(() => MutationFingerprintProfileV1.Compute(
                new CandidateApplyFingerprintInput(
                    Scope(), Id(11), Id(5), Id(6), Id(7),
                    "sha256:invalid", Id(12)))));

        run("FP-SEC-07 Uppercase digest rejected", () =>
            ExpectRejected(() => MutationFingerprintProfileV1.Compute(
                new CandidateApplyFingerprintInput(
                    Scope(), Id(11), Id(5), Id(6), Id(7),
                    "sha256:" + new string('A', 64), Id(12)))));

        run("FP-SEC-08 Missing target rejected", () =>
            ExpectRejected(() => MutationFingerprintProfileV1.Compute(
                CreateEdit(
                    PlanEditCommandTypes.MoveRoom,
                    """{"deltaX":1,"deltaY":2}""",
                    null))));

        run("FP-SEC-09 Duplicate root property rejected", () =>
            ExpectRejected(() => MutationFingerprintProfileV1.Compute(
                CreateEdit(
                    PlanEditCommandTypes.MoveRoom,
                    """{"deltaX":1,"deltaX":2,"deltaY":0}""",
                    Id(10)))));

        run("FP-SEC-10 Nested duplicate property rejected", () =>
            ExpectRejected(() => MutationFingerprintProfileV1.Compute(
                CreateEdit(
                    PlanEditCommandTypes.AddOpening,
                    """{"opening":{"id":"00000000-0000-0000-0000-000000000019","type":"door","wallId":"00000000-0000-0000-0000-000000000016","offset":0.5,"width":0.9,"width":1.5}}""",
                    null))));

        run("FP-SEC-11 Nonfinite number rejected", () =>
            ExpectRejected(() => MutationFingerprintProfileV1.Compute(
                CreateEdit(
                    PlanEditCommandTypes.MoveRoom,
                    """{"deltaX":1e400,"deltaY":0}""",
                    Id(10)))));

        run("FP-SEC-12 Unknown payload property rejected", () =>
            ExpectRejected(() => MutationFingerprintProfileV1.Compute(
                CreateEdit(
                    PlanEditCommandTypes.MoveRoom,
                    """{"deltaX":1,"deltaY":0,"extra":1}""",
                    Id(10)))));

        run("FP-SEC-13 Actor identity sensitivity", () =>
            DifferentFingerprint(
                MutationFingerprintProfileV1.Compute(baseline),
                MutationFingerprintProfileV1.Compute(
                    baseline with
                    {
                        Scope = Scope() with
                        {
                            AuthenticatedActorId = Id(99)
                        }
                    })));

        run("FP-SEC-14 Project identity sensitivity", () =>
            DifferentFingerprint(
                MutationFingerprintProfileV1.Compute(baseline),
                MutationFingerprintProfileV1.Compute(
                    baseline with
                    {
                        Scope = Scope() with { ProjectId = Id(99) }
                    })));

        run("FP-SEC-15 Authorization version sensitivity", () =>
            DifferentFingerprint(
                MutationFingerprintProfileV1.Compute(baseline),
                MutationFingerprintProfileV1.Compute(
                    baseline with
                    {
                        Scope = Scope() with
                        {
                            AuthorizationVersion =
                                new AuthorizationVersion(8)
                        }
                    })));

        run("FP-SEC-16 Operation identity sensitivity", () =>
            DifferentFingerprint(
                MutationFingerprintProfileV1.Compute(baseline),
                MutationFingerprintProfileV1.Compute(
                    baseline with
                    {
                        Scope = Scope() with { OperationId = Id(99) }
                    })));

        run("FP-SEC-17 Candidate identity sensitivity", () =>
            DifferentFingerprint(
                MutationFingerprintProfileV1.Compute(baseline),
                MutationFingerprintProfileV1.Compute(
                    baseline with { CandidateId = Id(99) })));

        run("FP-SEC-18 Requirement revision sensitivity", () =>
            DifferentFingerprint(
                MutationFingerprintProfileV1.Compute(move),
                MutationFingerprintProfileV1.Compute(
                    move with { RequirementsRevisionId = Id(99) })));

        run("FP-SEC-19 Land revision sensitivity", () =>
            DifferentFingerprint(
                MutationFingerprintProfileV1.Compute(move),
                MutationFingerprintProfileV1.Compute(
                    move with { LandRevisionId = Id(99) })));

        run("FP-SEC-20 JSON property order equivalence", () =>
            SameFingerprint(
                MutationFingerprintProfileV1.Compute(move),
                MutationFingerprintProfileV1.Compute(
                    CreateEdit(
                        PlanEditCommandTypes.MoveRoom,
                        """{ "deltaY": -0.5, "deltaX": 1.25 }""",
                        Id(10)))));

        run("FP-SEC-21 Negative zero equivalence", () =>
            SameFingerprint(
                MutationFingerprintProfileV1.Compute(
                    CreateEdit(
                        PlanEditCommandTypes.MoveRoom,
                        """{"deltaX":0,"deltaY":1}""",
                        Id(10))),
                MutationFingerprintProfileV1.Compute(
                    CreateEdit(
                        PlanEditCommandTypes.MoveRoom,
                        """{"deltaX":-0.0,"deltaY":1}""",
                        Id(10)))));

        run("FP-SEC-22 Unicode NFC equivalence", () =>
            SameFingerprint(
                MutationFingerprintProfileV1.Compute(
                    baseline with { RejectionReasonCode = "\u00E9" }),
                MutationFingerprintProfileV1.Compute(
                    baseline with { RejectionReasonCode = "e\u0301" })));

        run("FP-SEC-23 Absent versus empty optional value", () =>
            DifferentFingerprint(
                MutationFingerprintProfileV1.Compute(baseline),
                MutationFingerprintProfileV1.Compute(
                    baseline with { RejectionReasonCode = "" })));

        run("FP-SEC-24 Invalid Unicode surrogate rejected", () =>
            ExpectRejected(() => MutationFingerprintProfileV1.Compute(
                baseline with { RejectionReasonCode = "\uD800" })));
    }

    private static void RejectEncoding(byte[] bytes)
    {
        if (MutationFingerprintProfileV1.IsCanonicalEncoding(bytes))
            throw new Exception("Malformed outer encoding was accepted.");
    }

    private static void RegisterBoundaryTests(
        Action<string, Action> run)
    {
        var reject = new CandidateRejectFingerprintInput(
            Scope(), Id(11));

        var expire = new CandidateExpireFingerprintInput(
            Scope(), Id(11),
            DateTimeOffset.UnixEpoch.AddTicks(12_345_670));

        var apply = new CandidateApplyFingerprintInput(
            Scope(), Id(11), Id(5), Id(6), Id(7),
            GeometryHash, Id(12));

        var confirm = new FinalPlanConfirmationFingerprintInput(
            Scope(), Id(5), GeometryHash, Id(12),
            Id(6), Id(7), true);

        run("FP-BND-01 Exact independent reject bytes", () =>
        {
            var expectedBody = Convert.FromHexString(
                "000103" +
                "0010010000001000000000000000000000000000000001" +
                "0011010000001000000000000000000000000000000002" +
                "0012010000001000000000000000000000000000000003" +
                "0013010000001000000000000000000000000000000004" +
                "001401000000080000000000000007" +
                "0015010000000103" +
                "030001000000100000000000000000000000000000000b" +
                "0301010000000101" +
                "03020000000000");

            var header = System.Text.Encoding.ASCII.GetBytes(
                "BuildMind.F02.MutationFingerprint\0");

            var expected = header.Concat(expectedBody).ToArray();

            var actual =
                MutationFingerprintProfileV1.GetCanonicalBytes(reject);

            if (!actual.SequenceEqual(expected))
                throw new Exception("Exact byte comparison failed.");
        });

        run("FP-BND-02 Invalid domain header rejected", () =>
        {
            var bytes =
                MutationFingerprintProfileV1.GetCanonicalBytes(reject);

            bytes[0] ^= 0x01;
            RejectEncoding(bytes);
        });

        run("FP-BND-03 Truncated encoding rejected", () =>
        {
            var bytes =
                MutationFingerprintProfileV1.GetCanonicalBytes(reject);

            RejectEncoding(bytes[..^1]);
        });

        run("FP-BND-04 Missing optional field framing rejected", () =>
        {
            var bytes =
                MutationFingerprintProfileV1.GetCanonicalBytes(reject);

            RejectEncoding(bytes[..^7]);
        });

        run("FP-BND-05 Duplicate field tag rejected", () =>
        {
            var bytes =
                MutationFingerprintProfileV1.GetCanonicalBytes(reject);

            RejectEncoding(
                bytes.Concat(bytes[^7..]).ToArray());
        });

        run("FP-BND-06 Invalid presence indicator rejected", () =>
        {
            var bytes =
                MutationFingerprintProfileV1.GetCanonicalBytes(reject);

            bytes[^5] = 2;
            RejectEncoding(bytes);
        });

        run("FP-BND-07 Invalid field length rejected", () =>
        {
            var bytes =
                MutationFingerprintProfileV1.GetCanonicalBytes(reject);

            bytes[^1] = 1;
            RejectEncoding(bytes);
        });

        run("FP-BND-08 Invalid UTF8 rejected", () =>
        {
            var bytes =
                MutationFingerprintProfileV1.GetCanonicalBytes(
                    reject with { RejectionReasonCode = "A" });

            bytes[^1] = 0xFF;
            RejectEncoding(bytes);
        });

        run("FP-BND-09 Submicrosecond timestamp rejected", () =>
            ExpectRejected(() =>
                MutationFingerprintProfileV1.Compute(
                    expire with
                    {
                        ExpiresAtUtc = expire.ExpiresAtUtc.AddTicks(1)
                    })));

        run("FP-BND-10 Equivalent UTC timestamp offsets", () =>
            SameFingerprint(
                MutationFingerprintProfileV1.Compute(expire),
                MutationFingerprintProfileV1.Compute(
                    expire with
                    {
                        ExpiresAtUtc = expire.ExpiresAtUtc.ToOffset(
                            TimeSpan.FromHours(5.5))
                    })));

        run("FP-BND-11 Expiry timestamp sensitivity", () =>
            DifferentFingerprint(
                MutationFingerprintProfileV1.Compute(expire),
                MutationFingerprintProfileV1.Compute(
                    expire with
                    {
                        ExpiresAtUtc = expire.ExpiresAtUtc.AddTicks(10)
                    })));

        run("FP-BND-12 Geometry digest sensitivity", () =>
            DifferentFingerprint(
                MutationFingerprintProfileV1.Compute(apply),
                MutationFingerprintProfileV1.Compute(
                    apply with
                    {
                        CandidateGeometryHash =
                            "sha256:" + new string('f', 64)
                    })));

        run("FP-BND-13 Validation evidence sensitivity", () =>
            DifferentFingerprint(
                MutationFingerprintProfileV1.Compute(apply),
                MutationFingerprintProfileV1.Compute(
                    apply with { ValidationEvidenceId = Id(99) })));

        run("FP-BND-14 Warning acknowledgement sensitivity", () =>
            DifferentFingerprint(
                MutationFingerprintProfileV1.Compute(confirm),
                MutationFingerprintProfileV1.Compute(
                    confirm with { WarningAcknowledged = false })));

        run("FP-BND-15 Oversized collection rejected", () =>
        {
            var items = string.Join(",",
                Enumerable.Repeat("null", 10_001));

            var payload = "{\"walls\":[" + items + "]}";

            ExpectRejected(() =>
                MutationFingerprintProfileV1.Compute(
                    CreateEdit(
                        PlanEditCommandTypes.AddRoom,
                        payload,
                        null)));
        });
    }

    private static CandidateCreateFingerprintInput LargeRoom(
        int wallCount)
    {
        var walls = Enumerable.Range(100, wallCount)
            .Select(i => new
            {
                id = Id(i),
                start = new { x = 0, y = 0 },
                end = new { x = 4, y = 0 },
                thickness = 0.2
            }).ToArray();

        var payload = JsonSerializer.Serialize(new
        {
            room = new
            {
                id = Id(21),
                name = "Boundary room",
                type = "bedroom",
                polygon = new[]
                {
                    new { x = 0, y = 0 },
                    new { x = 4, y = 0 },
                    new { x = 0, y = 3 },
                    new { x = 0, y = 0 }
                },
                wallIds = new[] { Id(100), Id(101), Id(102) }
            },
            walls,
            openings = Array.Empty<object>()
        });

        return CreateEdit(
            PlanEditCommandTypes.AddRoom,
            payload,
            null);
    }

    private static void RegisterStressTests(
        Action<string, Action> run)
    {
        var move = CreateEdit(
            PlanEditCommandTypes.MoveRoom,
            """{"deltaX":1.25,"deltaY":-0.5}""",
            Id(10));

        run("FP-STR-01 Maximum supported wall collection", () =>
        {
            var input = LargeRoom(10_000);

            var timer = System.Diagnostics.Stopwatch.StartNew();

            var before = GC.GetAllocatedBytesForCurrentThread();

            var bytes =
                MutationFingerprintProfileV1.GetCanonicalBytes(input);

            var allocated =
                GC.GetAllocatedBytesForCurrentThread() - before;

            timer.Stop();

            var fingerprint = CommandFingerprint.Compute(bytes);

            if (!fingerprint.IsValid ||
                !MutationFingerprintProfileV1.IsCanonicalEncoding(bytes))
                throw new Exception("Maximum valid collection failed.");

            Console.WriteLine(
                $"STRESS: 10000 walls; bytes={bytes.Length}; " +
                $"allocated={allocated}; elapsedMs={timer.ElapsedMilliseconds}");
        });

        run("FP-STR-02 Valid-shaped oversized collection rejected", () =>
        {
            var input = LargeRoom(10_001);

            ExpectRejected(() =>
                MutationFingerprintProfileV1.Compute(input));
        });

        run("FP-STR-03 Repeated large payload deterministic", () =>
        {
            var input = LargeRoom(1_000);

            var expected =
                MutationFingerprintProfileV1.Compute(input);

            for (var i = 0; i < 10; i++)
            {
                SameFingerprint(
                    expected,
                    MutationFingerprintProfileV1.Compute(input));
            }
        });

        run("FP-STR-04 Concurrent deterministic fingerprints", () =>
        {
            var expected =
                MutationFingerprintProfileV1.Compute(move);

            Parallel.For(
                0, 128,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 4
                },
                _ =>
                {
                    SameFingerprint(
                        expected,
                        MutationFingerprintProfileV1.Compute(move));
                });

            Console.WriteLine(
                "STRESS: 128 concurrent computations verified.");
        });

        run("FP-STR-05 Concurrent distinct operation identities", () =>
        {
            var results = new string[128];

            Parallel.For(
                0, results.Length,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = 4
                },
                i =>
                {
                    var input = move with
                    {
                        Scope = Scope() with
                        {
                            OperationId = Id(1000 + i)
                        }
                    };

                    var fingerprint =
                        MutationFingerprintProfileV1.Compute(input);

                    if (!fingerprint.IsValid)
                        throw new Exception("Invalid concurrent digest.");

                    results[i] = fingerprint.Value!;
                });

            if (results.Distinct(StringComparer.Ordinal).Count()
                != results.Length)
                throw new Exception("Distinct operation identities collided.");
        });

        run("FP-STR-06 Optional null and absent anchor equivalence", () =>
        {
            SameFingerprint(
                MutationFingerprintProfileV1.Compute(
                    CreateEdit(
                        PlanEditCommandTypes.ResizeRoom,
                        """{"scaleX":1,"scaleY":1,"anchorX":null}""",
                        Id(10))),
                MutationFingerprintProfileV1.Compute(
                    CreateEdit(
                        PlanEditCommandTypes.ResizeRoom,
                        """{"scaleX":1,"scaleY":1}""",
                        Id(10))));
        });

        run("FP-STR-07 Escaped duplicate property rejected", () =>
        {
            ExpectRejected(() =>
                MutationFingerprintProfileV1.Compute(
                    CreateEdit(
                        PlanEditCommandTypes.MoveRoom,
                        """{"deltaX":1,"delta\u0058":2,"deltaY":0}""",
                        Id(10))));
        });

        run("FP-STR-08 Invalid nested null rejected", () =>
        {
            ExpectRejected(() =>
                MutationFingerprintProfileV1.Compute(
                    CreateEdit(
                        PlanEditCommandTypes.AddOpening,
                        """{"opening":null}""",
                        null)));
        });

        run("FP-STR-09 Plan identity sensitivity", () =>
        {
            DifferentFingerprint(
                MutationFingerprintProfileV1.Compute(move),
                MutationFingerprintProfileV1.Compute(
                    move with
                    {
                        Scope = Scope() with { PlanId = Id(99) }
                    }));
        });

        run("FP-STR-10 Base revision identity sensitivity", () =>
        {
            DifferentFingerprint(
                MutationFingerprintProfileV1.Compute(move),
                MutationFingerprintProfileV1.Compute(
                    move with
                    {
                        ExpectedBaseRevisionId = Id(99)
                    }));
        });
    }

    // Independent test-only reference construction.
    // Expected digests remain fixed golden-vector constants.
    private static byte[] ReferenceBytes(int vectorLength)
    {
        static byte[] Hex(string value) =>
            Convert.FromHexString(value);

        static byte[] Join(params byte[][] chunks) =>
            chunks.SelectMany(x => x).ToArray();

        static byte[] U(int value) =>
            Convert.FromHexString(value.ToString("x32"));

        static byte[] N(double value) =>
            Convert.FromHexString(
                BitConverter.DoubleToInt64Bits(value).ToString("x16"));

        static byte[] B(int value) => [(byte)value];

        static byte[] T(string value) =>
            System.Text.Encoding.UTF8.GetBytes(value);

        static byte[] F(int tag, byte[]? value)
        {
            var header = Hex(
                tag.ToString("x4") +
                (value is null ? "00" : "01") +
                (value?.Length ?? 0).ToString("x8"));

            return Join(header, value ?? []);
        }

        static byte[] Fields(params byte[]?[] values) =>
            Join(values.Select((value, index) =>
                F(index + 1, value)).ToArray());

        static byte[] Seq(params byte[][] values) =>
            Join(
                Hex(values.Length.ToString("x8")),
                Join(values.Select(value =>
                    Join(Hex(value.Length.ToString("x8")),
                        value)).ToArray()));

        static byte[] Outer(int operation, params byte[]?[] values)
        {
            var common = Join(
                T("BuildMind.F02.MutationFingerprint\0"),
                Hex("0001"),
                B(operation),
                F(0x0010, U(1)),
                F(0x0011, U(2)),
                F(0x0012, U(3)),
                F(0x0013, U(4)),
                F(0x0014, Hex("0000000000000007")),
                F(0x0015, B(operation)));

            return Join(common,
                Join(values.Select((value, index) =>
                    F(operation * 256 + index, value))
                    .ToArray()));
        }

        static byte[] Create(
            int code, byte[] payload, bool target = true) =>
            Outer(1,
                U(5), U(6), U(7), T("0.1-draft"),
                U(8), B(code), U(9),
                target ? U(10) : null,
                Join(B(code), payload));

        static byte[] P(double x, double y) =>
            Fields(N(x), N(y));

        static byte[] W(int id, double x1, double y1,
            double x2, double y2) =>
            Fields(U(id), P(x1, y1), P(x2, y2), N(0.2));

        var geometry = Hex(
            "000102030405060708090a0b0c0d0e0f" +
            "101112131415161718191a1b1c1d1e1f");

        return vectorLength switch
        {
            // CandidateCreate / MoveRoom
            352 => Create(3, Fields(N(1.25), N(-0.5))),

            // CandidateApply
            314 => Outer(2, U(11), B(1),
                U(5), U(6), U(7), geometry, U(12)),

            // CandidateReject
            190 => Outer(3, U(11), B(1), null),

            // CandidateExpire
            198 => Outer(4, U(11), B(1),
                Hex("000000000012d687")),

            // PlanRevisionAppend
            299 => Outer(5, U(5), geometry,
                U(6), U(7), U(12), T("0.1-draft")),

            // FinalPlanConfirmation
            291 => Outer(6, U(5), geometry, U(12),
                U(6), U(7), B(1)),

            // AddRoom
            949 => Create(1,
                Fields(
                    Fields(
                        U(21), T("Room"), T("bedroom"),
                        Seq(P(0, 0), P(4, 0), P(0, 3), P(0, 0)),
                        Seq(U(22), U(23), U(24))),
                    Seq(
                        W(22, 0, 0, 4, 0),
                        W(23, 4, 0, 0, 3),
                        W(24, 0, 3, 0, 0)),
                    Seq()),
                false),

            // RemoveRoom
            330 => Create(2, Fields(B(1))),

            // ResizeRoom
            374 => Create(4,
                Fields(N(1.2), N(0.8), null, N(0))),

            // UpdateWall
            365 => Create(5,
                Fields(null, null, null, null, N(0.25))),

            // AddOpening
            400 => Create(6,
                Fields(Fields(
                    U(25), T("door"), U(22),
                    N(0.5), N(0.9))),
                false),

            // MoveOpening
            337 => Create(7, Fields(N(1.5))),

            // RemoveOpening
            322 => Create(8, []),

            _ => throw new Exception(
                "Unknown reference fixture.")
        };
    }

    private static void Check(
        byte[] canonical,
        CommandFingerprint fingerprint,
        int expectedLength,
        string expectedDigest)
    {
        if (canonical.Length != expectedLength)
            throw new Exception(
                $"Length mismatch: expected {expectedLength}, actual {canonical.Length}.");

        if (!fingerprint.IsValid)
            throw new Exception("Invalid fingerprint format.");

        var referenceBytes = ReferenceBytes(expectedLength);

        var referenceDigest = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(referenceBytes))
            .ToLowerInvariant();

        if (!string.Equals(referenceDigest, expectedDigest,
                StringComparison.Ordinal))
            throw new Exception("Reference fixture digest mismatch.");

        if (!canonical.SequenceEqual(referenceBytes))
            throw new Exception("Exact canonical byte comparison failed.");

        Console.WriteLine(
            $"EXACT BYTES PASS: {canonical.Length} bytes");

        var expected = "sha256:" + expectedDigest;

        if (!string.Equals(fingerprint.Value, expected,
                StringComparison.Ordinal))
            throw new Exception(
                $"Golden digest mismatch: expected {expected}, actual {fingerprint.Value}.");

        var actual = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(canonical))
            .ToLowerInvariant();

        if (!string.Equals(actual, expectedDigest,
                StringComparison.Ordinal))
            throw new Exception("Canonical bytes digest mismatch.");

        if (!MutationFingerprintProfileV1.IsCanonicalEncoding(canonical))
            throw new Exception("Outer canonical framing was rejected.");

        Console.WriteLine($"GOLDEN VECTOR PASS: {expected}");
    }
}
