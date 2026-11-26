using System.Text.Json;
using BuildMind.Application.Features.PlanEditor.Confirmation;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Editing;
using BuildMind.Application.Features.PlanEditor.Versioning;

namespace BuildMind.Application.ContractTests.Integration;

internal static class PersistenceContractScenarios
{
    public static void Register(Action<string, Action> run)
    {
        run("persistence conformance appends revision and preserves history", () =>
        {
            var fixture = new Fixture();
            var original = fixture.Store.Current;
            var candidate = fixture.AddCandidate();
            var result = fixture.Store.TryApplyAtomicallyAsync(candidate, fixture.Proposed(candidate),
                fixture.Now, default).Result;
            Check(result.Status == CandidateActionStatus.Applied, "Candidate apply failed.");
            Check(fixture.Store.RevisionCount == 2, "Exactly one revision was not appended.");
            Check(fixture.Store.GetRevision(original.RevisionId) == original,
                "Historical revision was overwritten.");
        });

        run("persistence failure before revision insert changes nothing", () =>
            VerifyApplyRollback(ApplyFailurePoint.BeforeRevisionInsert));

        run("persistence failure after revision insert rolls back staged revision", () =>
            VerifyApplyRollback(ApplyFailurePoint.AfterRevisionInsert));

        run("persistence failure before pointer update rolls back", () =>
            VerifyApplyRollback(ApplyFailurePoint.BeforePointerUpdate));

        run("candidate consumption failure rolls back revision and pointer", () =>
            VerifyApplyRollback(ApplyFailurePoint.BeforeCandidateConsumption));

        run("duplicate candidate application returns prior terminal outcome", () =>
        {
            var fixture = new Fixture();
            var candidate = fixture.AddCandidate();
            var proposed = fixture.Proposed(candidate);
            Check(fixture.Store.TryApplyAtomicallyAsync(candidate, proposed, fixture.Now, default).Result.Status
                == CandidateActionStatus.Applied, "Initial apply failed.");
            Check(fixture.Store.TryApplyAtomicallyAsync(candidate, proposed, fixture.Now, default).Result.Status
                == CandidateActionStatus.AlreadyApplied, "Duplicate candidate application was accepted.");
            Check(fixture.Store.RevisionCount == 2, "Duplicate candidate created another revision.");
        });

        run("persistence concurrent candidate application has one winner", () =>
        {
            var fixture = new Fixture();
            var candidate = fixture.AddCandidate();
            var proposed = fixture.Proposed(candidate);
            var results = Task.WhenAll(
                fixture.Store.TryApplyAtomicallyAsync(candidate, proposed, fixture.Now, default),
                fixture.Store.TryApplyAtomicallyAsync(candidate, proposed, fixture.Now, default)).Result;
            Check(results.Count(x => x.Status == CandidateActionStatus.Applied) == 1,
                "Concurrent apply lacked exactly one winner.");
            Check(results.Count(x => x.Status == CandidateActionStatus.AlreadyApplied) == 1,
                "Concurrent loser did not reconcile terminal state.");
            Check(fixture.Store.RevisionCount == 2, "Concurrent apply created multiple revisions.");
        });

        run("persistence apply versus reject race has one terminal outcome", () =>
        {
            var fixture = new Fixture();
            var candidate = fixture.AddCandidate();
            var apply = Task.Run(() => fixture.Store.TryApplyAtomicallyAsync(
                candidate, fixture.Proposed(candidate), fixture.Now, default).Result);
            var reject = Task.Run(() => fixture.Store.TryRejectAtomicallyAsync(
                candidate, fixture.Now, default).Result);
            Task.WaitAll(apply, reject);
            var winners = new[] { apply.Result.Status, reject.Result.Status }
                .Count(x => x is CandidateActionStatus.Applied or CandidateActionStatus.Rejected);
            Check(winners == 1, "Apply/Reject race had multiple winners.");
            Check(fixture.Store.GetCandidate(candidate.CandidateId)!.Status is
                CandidatePreviewStatus.Applied or CandidatePreviewStatus.Rejected,
                "Race did not persist one terminal state.");
        });

        run("duplicate confirmation is idempotently rejected", () =>
        {
            var fixture = new Fixture();
            var confirmation = fixture.Confirmation();
            Check(fixture.Store.ConfirmAtomicallyAsync(confirmation, fixture.Store.Current, default).Result.Status
                == ConfirmationStatus.Confirmed, "Initial confirmation failed.");
            Check(fixture.Store.ConfirmAtomicallyAsync(confirmation, fixture.Store.Current, default).Result.Status
                == ConfirmationStatus.AlreadyConfirmed, "Duplicate confirmation was inserted.");
            Check(fixture.Store.ConfirmationCount == 1 && fixture.Store.OutboxCount == 1,
                "Duplicate confirmation duplicated durable records.");
        });

        run("confirmation lifecycle failure rolls back confirmation and outbox", () =>
        {
            var fixture = new Fixture();
            fixture.Store.ConfirmationFailure = ConfirmationFailurePoint.LifecycleTransition;
            CheckThrows<InvalidOperationException>(() => fixture.Store.ConfirmAtomicallyAsync(
                fixture.Confirmation(), fixture.Store.Current, default).GetAwaiter().GetResult());
            Check(fixture.Store.ConfirmationCount == 0 && fixture.Store.OutboxCount == 0,
                "Lifecycle failure left confirmation/outbox state.");
            Check(fixture.Store.ProjectState == "EDITING", "Lifecycle failure changed project state.");
        });

        run("confirmation outbox insertion failure rolls back lifecycle and confirmation", () =>
        {
            var fixture = new Fixture();
            fixture.Store.ConfirmationFailure = ConfirmationFailurePoint.OutboxInsert;
            CheckThrows<InvalidOperationException>(() => fixture.Store.ConfirmAtomicallyAsync(
                fixture.Confirmation(), fixture.Store.Current, default).GetAwaiter().GetResult());
            Check(fixture.Store.ConfirmationCount == 0 && fixture.Store.OutboxCount == 0,
                "Outbox failure left partial confirmation state.");
            Check(fixture.Store.ProjectState == "EDITING", "Outbox failure left lifecycle transition.");
        });

        run("post-commit report publication failure does not roll back confirmation", () =>
        {
            var fixture = new Fixture();
            var confirmation = fixture.Confirmation();
            Check(fixture.Store.ConfirmAtomicallyAsync(confirmation, fixture.Store.Current, default).Result.Status
                == ConfirmationStatus.Confirmed, "Confirmation transaction failed.");
            CheckThrows<InvalidOperationException>(() => fixture.Store.PublishReport(confirmation.ConfirmationId));
            Check(fixture.Store.ConfirmationCount == 1 && fixture.Store.OutboxCount == 1 &&
                fixture.Store.ProjectState == "CONFIRMED", "Post-commit failure rolled back durable state.");
        });

        run("duplicate outbox event consumption is idempotent", () =>
        {
            var fixture = new Fixture();
            var confirmation = fixture.Confirmation();
            fixture.Store.ConfirmAtomicallyAsync(confirmation, fixture.Store.Current, default).Wait();
            var messageId = fixture.Store.SingleOutboxMessageId;
            Check(fixture.Store.ConsumeOutbox(messageId), "First outbox delivery was not consumed.");
            Check(!fixture.Store.ConsumeOutbox(messageId), "Duplicate outbox delivery created another outcome.");
            Check(fixture.Store.ReportCount == 1, "Duplicate delivery created multiple reports.");
        });

        run("uncertain candidate commit outcome reconciles by candidate identity", () =>
        {
            var fixture = new Fixture();
            var candidate = fixture.AddCandidate();
            fixture.Store.ApplyFailure = ApplyFailurePoint.AfterCommitAcknowledgementLost;
            CheckThrows<UncertainCommitTestException>(() => fixture.Store.TryApplyAtomicallyAsync(
                candidate, fixture.Proposed(candidate), fixture.Now, default).GetAwaiter().GetResult());
            var reconciled = fixture.Store.GetCandidate(candidate.CandidateId);
            Check(reconciled?.Status == CandidatePreviewStatus.Applied && fixture.Store.RevisionCount == 2,
                "Uncertain committed outcome could not be reconciled.");
        });
    }

    private static void VerifyApplyRollback(ApplyFailurePoint point)
    {
        var fixture = new Fixture();
        var candidate = fixture.AddCandidate();
        var originalPointer = fixture.Store.Current.RevisionId;
        fixture.Store.ApplyFailure = point;
        CheckThrows<InvalidOperationException>(() => fixture.Store.TryApplyAtomicallyAsync(
            candidate, fixture.Proposed(candidate), fixture.Now, default).GetAwaiter().GetResult());
        Check(fixture.Store.RevisionCount == 1, "Failed transaction retained a staged revision.");
        Check(fixture.Store.Current.RevisionId == originalPointer, "Failed transaction changed current pointer.");
        Check(fixture.Store.GetCandidate(candidate.CandidateId)!.Status == CandidatePreviewStatus.Pending,
            "Failed transaction consumed candidate.");
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

    private sealed class Fixture
    {
        public DateTimeOffset Now { get; } = DateTimeOffset.Parse("2026-11-17T12:00:00Z");
        public Guid ProjectId { get; } = Guid.NewGuid();
        public Guid PlanId { get; } = Guid.NewGuid();
        public Guid OwnerId { get; } = Guid.NewGuid();
        public AtomicPersistenceTestDouble Store { get; }

        public Fixture()
        {
            var geometry = Geometry(ProjectId, PlanId);
            var json = JsonSerializer.Serialize(geometry, PlanEditorJson.StrictOptions);
            var hash = PlanRevisionService.ComputeGeometryHash(json);
            Store = new AtomicPersistenceTestDouble(new(
                Guid.NewGuid(), PlanId, ProjectId, null, 1, geometry.SchemaVersion, json, hash,
                geometry.LandRevisionId, geometry.RequirementsRevisionId, Validation(hash), null,
                OwnerId, Now, "base"));
        }

        public CandidatePreviewSnapshot AddCandidate()
        {
            var current = Store.Current;
            var candidate = new CandidatePreviewSnapshot(
                Guid.NewGuid(), ProjectId, PlanId, OwnerId, current.RevisionId,
                current.LandRevisionId, current.RequirementsRevisionId, Guid.NewGuid(),
                current.GeometryJson, current.GeometryHash, current.GeometrySchemaVersion,
                current.Validation, Now, Now.AddMinutes(15), CandidatePreviewStatus.Pending);
            Store.CreateAsync(candidate, default).Wait();
            return candidate;
        }

        public PlanRevisionSnapshot Proposed(CandidatePreviewSnapshot candidate) => new(
            Guid.NewGuid(), PlanId, ProjectId, candidate.BaseRevisionId, 2,
            candidate.GeometrySchemaVersion, candidate.GeometryJson, candidate.GeometryHash,
            candidate.LandRevisionId, candidate.RequirementsRevisionId, candidate.Validation,
            candidate.CommandId, OwnerId, Now.AddSeconds(1), "candidate apply");

        public PlanConfirmationSnapshot Confirmation() => new(
            Guid.NewGuid(), ProjectId, PlanId, Store.Current.RevisionId, Store.Current.GeometryHash,
            Store.Current.Validation.ValidationId, OwnerId, Now, true);
    }

    private enum ApplyFailurePoint
    {
        None,
        BeforeRevisionInsert,
        AfterRevisionInsert,
        BeforePointerUpdate,
        BeforeCandidateConsumption,
        AfterCommitAcknowledgementLost
    }

    private enum ConfirmationFailurePoint { None, LifecycleTransition, OutboxInsert }

    private sealed class UncertainCommitTestException : Exception
    {
        public UncertainCommitTestException() : base("Explicit uncertain commit test double.") { }
    }

    private sealed class AtomicPersistenceTestDouble(PlanRevisionSnapshot initial)
        : IPlanRevisionStore, ICandidatePreviewStore, ICandidatePreviewTransaction, IFinalConfirmationTransaction
    {
        private readonly object sync = new();
        private readonly Dictionary<Guid, PlanRevisionSnapshot> revisions = new() { [initial.RevisionId] = initial };
        private readonly Dictionary<Guid, CandidatePreviewSnapshot> candidates = [];
        private readonly Dictionary<Guid, PlanConfirmationSnapshot> confirmations = [];
        private readonly Dictionary<Guid, Guid> outbox = [];
        private readonly HashSet<Guid> consumedMessages = [];
        private Guid currentRevisionId = initial.RevisionId;
        private int reports;

        public ApplyFailurePoint ApplyFailure { get; set; }
        public ConfirmationFailurePoint ConfirmationFailure { get; set; }
        public string ProjectState { get; private set; } = "EDITING";
        public int RevisionCount { get { lock (sync) return revisions.Count; } }
        public int ConfirmationCount { get { lock (sync) return confirmations.Count; } }
        public int OutboxCount { get { lock (sync) return outbox.Count; } }
        public int ReportCount { get { lock (sync) return reports; } }
        public Guid SingleOutboxMessageId { get { lock (sync) return outbox.Keys.Single(); } }
        public PlanRevisionSnapshot Current { get { lock (sync) return revisions[currentRevisionId]; } }

        public PlanRevisionSnapshot? GetRevision(Guid id) { lock (sync) return revisions.GetValueOrDefault(id); }
        public CandidatePreviewSnapshot? GetCandidate(Guid id) { lock (sync) return candidates.GetValueOrDefault(id); }

        public Task CreateAsync(CandidatePreviewSnapshot candidate, CancellationToken cancellationToken)
        {
            lock (sync) candidates.Add(candidate.CandidateId, candidate);
            return Task.CompletedTask;
        }

        public Task<CandidatePreviewSnapshot?> GetAsync(Guid candidateId, CancellationToken cancellationToken)
        {
            lock (sync) return Task.FromResult(candidates.GetValueOrDefault(candidateId));
        }

        public Task<CandidateActionResult> TryApplyAtomicallyAsync(
            CandidatePreviewSnapshot expected, PlanRevisionSnapshot proposed, DateTimeOffset now,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                var currentCandidate = candidates.GetValueOrDefault(expected.CandidateId);
                if (currentCandidate is null) return Task.FromResult(new CandidateActionResult(CandidateActionStatus.NotFound));
                if (currentCandidate.Status == CandidatePreviewStatus.Applied)
                    return Task.FromResult(new CandidateActionResult(CandidateActionStatus.AlreadyApplied, currentCandidate));
                if (currentCandidate.Status == CandidatePreviewStatus.Rejected)
                    return Task.FromResult(new CandidateActionResult(CandidateActionStatus.AlreadyRejected, currentCandidate));
                if (currentCandidate.Status != CandidatePreviewStatus.Pending || now >= currentCandidate.ExpiresAtUtc)
                    return Task.FromResult(new CandidateActionResult(CandidateActionStatus.Expired, currentCandidate));
                if (currentRevisionId != expected.BaseRevisionId)
                    return Task.FromResult(new CandidateActionResult(CandidateActionStatus.StaleBaseRevision, currentCandidate));

                FailAt(ApplyFailurePoint.BeforeRevisionInsert);
                var stagedRevisions = new Dictionary<Guid, PlanRevisionSnapshot>(revisions)
                {
                    [proposed.RevisionId] = proposed
                };
                FailAt(ApplyFailurePoint.AfterRevisionInsert);
                FailAt(ApplyFailurePoint.BeforePointerUpdate);
                var stagedPointer = proposed.RevisionId;
                FailAt(ApplyFailurePoint.BeforeCandidateConsumption);
                var applied = currentCandidate with { Status = CandidatePreviewStatus.Applied };

                revisions.Clear();
                foreach (var item in stagedRevisions) revisions.Add(item.Key, item.Value);
                currentRevisionId = stagedPointer;
                candidates[expected.CandidateId] = applied;
                if (ApplyFailure == ApplyFailurePoint.AfterCommitAcknowledgementLost)
                    throw new UncertainCommitTestException();
                return Task.FromResult(new CandidateActionResult(CandidateActionStatus.Applied, applied, proposed));
            }
        }

        private void FailAt(ApplyFailurePoint point)
        {
            if (ApplyFailure == point) throw new InvalidOperationException($"Explicit failure at {point}.");
        }

        public Task<CandidateActionResult> TryRejectAtomicallyAsync(
            CandidatePreviewSnapshot expected, DateTimeOffset now, CancellationToken cancellationToken)
        {
            lock (sync)
            {
                var candidate = candidates.GetValueOrDefault(expected.CandidateId);
                if (candidate?.Status == CandidatePreviewStatus.Applied)
                    return Task.FromResult(new CandidateActionResult(CandidateActionStatus.AlreadyApplied, candidate));
                if (candidate?.Status == CandidatePreviewStatus.Rejected)
                    return Task.FromResult(new CandidateActionResult(CandidateActionStatus.AlreadyRejected, candidate));
                if (candidate is null) return Task.FromResult(new CandidateActionResult(CandidateActionStatus.NotFound));
                var rejected = candidate with { Status = CandidatePreviewStatus.Rejected };
                candidates[candidate.CandidateId] = rejected;
                return Task.FromResult(new CandidateActionResult(CandidateActionStatus.Rejected, rejected));
            }
        }

        public Task<CandidateActionResult> TryExpireAtomicallyAsync(
            CandidatePreviewSnapshot expected, DateTimeOffset now, CancellationToken cancellationToken)
        {
            lock (sync)
            {
                var candidate = candidates.GetValueOrDefault(expected.CandidateId);
                if (candidate is null) return Task.FromResult(new CandidateActionResult(CandidateActionStatus.NotFound));
                var expired = candidate with { Status = CandidatePreviewStatus.Expired };
                candidates[candidate.CandidateId] = expired;
                return Task.FromResult(new CandidateActionResult(CandidateActionStatus.Expired, expired));
            }
        }

        public Task<ConfirmationResult> ConfirmAtomicallyAsync(
            PlanConfirmationSnapshot confirmation, PlanRevisionSnapshot exactRevision,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                if (confirmations.Values.Any(x => x.PlanId == confirmation.PlanId &&
                    x.ConfirmedRevisionId == confirmation.ConfirmedRevisionId))
                    return Task.FromResult(new ConfirmationResult(ConfirmationStatus.AlreadyConfirmed));
                if (ConfirmationFailure == ConfirmationFailurePoint.LifecycleTransition)
                    throw new InvalidOperationException("Explicit lifecycle test failure.");
                var stagedConfirmation = confirmation;
                var stagedState = "CONFIRMED";
                if (ConfirmationFailure == ConfirmationFailurePoint.OutboxInsert)
                    throw new InvalidOperationException("Explicit outbox test failure.");
                var messageId = Guid.NewGuid();
                confirmations.Add(stagedConfirmation.ConfirmationId, stagedConfirmation);
                ProjectState = stagedState;
                outbox.Add(messageId, stagedConfirmation.ConfirmationId);
                return Task.FromResult(new ConfirmationResult(ConfirmationStatus.Confirmed, confirmation));
            }
        }

        public void PublishReport(Guid confirmationId) =>
            throw new InvalidOperationException("Explicit post-commit report failure.");

        public bool ConsumeOutbox(Guid messageId)
        {
            lock (sync)
            {
                if (!outbox.ContainsKey(messageId) || !consumedMessages.Add(messageId)) return false;
                reports++;
                return true;
            }
        }

        public Task<PlanRevisionSnapshot?> GetAsync(
            Guid projectId, Guid planId, Guid revisionId, CancellationToken cancellationToken)
        {
            lock (sync) return Task.FromResult(revisions.GetValueOrDefault(revisionId));
        }

        public Task<PlanRevisionSnapshot?> GetCurrentAsync(
            Guid projectId, Guid planId, CancellationToken cancellationToken)
        {
            lock (sync) return Task.FromResult<PlanRevisionSnapshot?>(revisions[currentRevisionId]);
        }

        public Task<IReadOnlyList<PlanRevisionSnapshot>> GetHistoryAsync(
            Guid projectId, Guid planId, CancellationToken cancellationToken)
        {
            lock (sync) return Task.FromResult<IReadOnlyList<PlanRevisionSnapshot>>(revisions.Values.ToArray());
        }

        public Task<AppendRevisionResult> TryAppendAtomicallyAsync(
            Guid expectedBaseRevisionId, PlanRevisionSnapshot proposedRevision,
            CancellationToken cancellationToken)
        {
            lock (sync)
            {
                if (currentRevisionId != expectedBaseRevisionId)
                    return Task.FromResult(new AppendRevisionResult(AppendRevisionStatus.RevisionConflict));
                if (proposedRevision.EditCommandId is not null && revisions.Values.Any(
                    x => x.PlanId == proposedRevision.PlanId && x.EditCommandId == proposedRevision.EditCommandId))
                    return Task.FromResult(new AppendRevisionResult(AppendRevisionStatus.DuplicateCommand));
                revisions.Add(proposedRevision.RevisionId, proposedRevision);
                currentRevisionId = proposedRevision.RevisionId;
                return Task.FromResult(new AppendRevisionResult(AppendRevisionStatus.Created, proposedRevision));
            }
        }
    }

    private static PlanGeometryDto Geometry(Guid projectId, Guid planId) => new()
    {
        SchemaVersion = GeometrySchemaInfo.DraftVersion,
        ProjectId = projectId,
        PlanId = planId,
        LandRevisionId = Guid.NewGuid(),
        RequirementsRevisionId = Guid.NewGuid(),
        CoordinateSystem = new CoordinateSystemDto
        {
            Type = "localCartesian", Unit = "m", Origin = new CoordinatePointDto { X = 0, Y = 0 },
            XDirection = "right", YDirection = "up"
        },
        Floors = [new FloorDto { Id = Guid.NewGuid(), Level = 0, Rooms = [], Walls = [], Openings = [] }]
    };

    private static PlanValidationReferenceDto Validation(string hash) => new()
    {
        ValidationId = Guid.NewGuid(), ValidatorVersion = "persistence-conformance-double/1",
        GeometryHash = hash, GeometryValid = true, RequirementsMatch = true,
        RegulatoryEvidenceStatus = "VERIFIED", HasBlockingIssues = false,
        ValidatedAtUtc = DateTimeOffset.Parse("2026-11-17T12:00:00Z")
    };
}
