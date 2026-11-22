using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Contracts;
using BuildMind.Application.Features.Requirements.Integration;
using BuildMind.Domain.Entities;
using BuildMind.Domain.ValueObjects;
using F = BuildMind.Requirements.Tests.Fixtures.SyntheticRequirementFixtures;

namespace BuildMind.Requirements.Tests.Integration;

/// <summary>
/// M5-owned isolated security QA preparation and contract invariant tests.
/// Uses an in-memory simulated atomic gate to exercise security scenarios without touching
/// shared persistence, DI, or live databases.
/// IMPORTANT: Simulated in-memory locking does NOT prove MySQL/database transaction atomicity.
/// </summary>
public sealed class IsolatedAtomicRequirementSecurityScenariosTests
{
    private static readonly Guid OwnerA = Guid.Parse("70000000-0000-0000-0000-0000000000a1");
    private static readonly Guid OwnerB = Guid.Parse("70000000-0000-0000-0000-0000000000b2");
    private static readonly Guid ProjectId = Guid.Parse("60000000-0000-0000-0000-000000000061");

    #region Additional Contract Invariant Tests

    [Fact]
    public void Scope_RecordValueEquality_MatchesIdenticalInputsAndDistinguishesDifferences()
    {
        var project = Guid.NewGuid();
        var actor = Guid.NewGuid();
        var operation = Guid.NewGuid();
        var expected = Guid.NewGuid();

        var a = new IsolatedRequirementMutationScope(project, actor, operation, expected);
        var b = new IsolatedRequirementMutationScope(project, actor, operation, expected);
        var differentOp = new IsolatedRequirementMutationScope(project, actor, Guid.NewGuid(), expected);
        var nullExpected = new IsolatedRequirementMutationScope(project, actor, operation, null);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, differentOp);
        Assert.NotEqual(a, nullExpected);
    }

    [Fact]
    public void Outcome_RecordValueEquality_AndNegativeStatusRejection()
    {
        var rev = Guid.NewGuid();
        var s1 = new IsolatedRequirementMutationOutcome(IsolatedRequirementMutationStatus.Saved, rev);
        var s2 = new IsolatedRequirementMutationOutcome(IsolatedRequirementMutationStatus.Saved, rev);
        var s3 = new IsolatedRequirementMutationOutcome(IsolatedRequirementMutationStatus.Saved, Guid.NewGuid());
        var u1 = new IsolatedRequirementMutationOutcome(IsolatedRequirementMutationStatus.CommitOutcomeUnknown);

        Assert.Equal(s1, s2);
        Assert.NotEqual(s1, s3);
        Assert.NotEqual(s1, u1);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new IsolatedRequirementMutationOutcome((IsolatedRequirementMutationStatus)(-1)));
    }

    #endregion

    #region Positive & Negative Isolated Security Scenarios (Simulated)

    [Fact]
    public async Task Positive_TrustedOwnerWithMatchingVersionAndExpectedRevision_SavesAndPreservesHistory()
    {
        var gate = new SimulatedAtomicRequirementGate(ProjectId, OwnerA, new AuthorizationVersion(1));
        var actor = new SimulatedCurrentUser(OwnerA, isAuthenticated: true);

        var op1 = Guid.NewGuid();
        var first = await gate.ExecuteValidatedSaveAsync(
            actor,
            new IsolatedRequirementMutationScope(ProjectId, OwnerA, op1, null),
            new AuthorizationVersion(1),
            F.Request(),
            CancellationToken.None);

        Assert.Equal(IsolatedRequirementMutationStatus.Saved, first.Status);
        Assert.NotNull(first.SavedRequirementRevisionId);
        Assert.Equal(1, gate.HistoryCount);

        var op2 = Guid.NewGuid();
        var second = await gate.ExecuteValidatedSaveAsync(
            actor,
            new IsolatedRequirementMutationScope(ProjectId, OwnerA, op2, first.SavedRequirementRevisionId),
            new AuthorizationVersion(1),
            F.Request(expected: first.SavedRequirementRevisionId, method: "METHOD_A"),
            CancellationToken.None);

        Assert.Equal(IsolatedRequirementMutationStatus.Saved, second.Status);
        Assert.NotNull(second.SavedRequirementRevisionId);
        Assert.NotEqual(first.SavedRequirementRevisionId, second.SavedRequirementRevisionId);
        Assert.Equal(2, gate.HistoryCount);
        Assert.Equal(second.SavedRequirementRevisionId, gate.CurrentRevisionId);
    }

    [Theory]
    [InlineData(false, false, false)] // Unauthenticated
    [InlineData(true, true, false)]   // Empty actor ID
    [InlineData(true, false, true)]   // Inconsistent UserId vs GetRequiredUserId
    public async Task TrustedActorIdentityValidation_InvalidOrInconsistentIdentity_FailsClosedWithoutMutation(
        bool isAuthenticated, bool emptyActor, bool mismatchedRequiredId)
    {
        var gate = new SimulatedAtomicRequirementGate(ProjectId, OwnerA, new AuthorizationVersion(1));
        var actor = new SimulatedCurrentUser(
            emptyActor ? Guid.Empty : OwnerA,
            isAuthenticated,
            mismatchedRequiredId ? OwnerB : null);

        var scope = new IsolatedRequirementMutationScope(ProjectId, OwnerA, Guid.NewGuid(), null);
        var outcome = await gate.ExecuteValidatedSaveAsync(
            actor,
            scope,
            new AuthorizationVersion(1),
            F.Request(),
            CancellationToken.None);

        Assert.Equal(IsolatedRequirementMutationStatus.AccessDenied, outcome.Status);
        Assert.Null(outcome.SavedRequirementRevisionId);
        Assert.Equal(0, gate.HistoryCount);
    }

    [Fact]
    public async Task CrossOwnerDenial_NonOwnerActor_IsDeniedWithoutDisclosingExistingRevision()
    {
        var gate = new SimulatedAtomicRequirementGate(ProjectId, OwnerA, new AuthorizationVersion(1));
        var ownerUser = new SimulatedCurrentUser(OwnerA, isAuthenticated: true);
        var initial = await gate.ExecuteValidatedSaveAsync(
            ownerUser,
            new IsolatedRequirementMutationScope(ProjectId, OwnerA, Guid.NewGuid(), null),
            new AuthorizationVersion(1),
            F.Request(method: "METHOD_A"),
            CancellationToken.None);
        Assert.Equal(IsolatedRequirementMutationStatus.Saved, initial.Status);

        var attacker = new SimulatedCurrentUser(OwnerB, isAuthenticated: true);
        var denied = await gate.ExecuteValidatedSaveAsync(
            attacker,
            new IsolatedRequirementMutationScope(ProjectId, OwnerB, Guid.NewGuid(), initial.SavedRequirementRevisionId),
            new AuthorizationVersion(1),
            F.Request(expected: initial.SavedRequirementRevisionId),
            CancellationToken.None);

        Assert.Equal(IsolatedRequirementMutationStatus.AccessDenied, denied.Status);
        Assert.Null(denied.SavedRequirementRevisionId);
        Assert.Equal(1, gate.HistoryCount);
        Assert.Equal(initial.SavedRequirementRevisionId, gate.CurrentRevisionId);
    }

    [Fact]
    public async Task AuthorizationVersionMismatch_StaleVersion_IsRejectedWithoutMutation()
    {
        var gate = new SimulatedAtomicRequirementGate(ProjectId, OwnerA, new AuthorizationVersion(2));
        var actor = new SimulatedCurrentUser(OwnerA, isAuthenticated: true);

        var outcome = await gate.ExecuteValidatedSaveAsync(
            actor,
            new IsolatedRequirementMutationScope(ProjectId, OwnerA, Guid.NewGuid(), null),
            new AuthorizationVersion(1), // stale vs current v2
            F.Request(),
            CancellationToken.None);

        Assert.Equal(IsolatedRequirementMutationStatus.AuthorizationChanged, outcome.Status);
        Assert.Null(outcome.SavedRequirementRevisionId);
        Assert.Equal(0, gate.HistoryCount);
    }

    [Theory]
    [InlineData(false)] // Revoked before execution
    [InlineData(true)]  // Revoked mid-flight (simulated TOCTOU before atomic commit)
    public async Task OwnershipRevocation_BeforeOrDuringMutation_IsRejectedAtomicallyWithoutWrite(bool midFlight)
    {
        var gate = new SimulatedAtomicRequirementGate(ProjectId, OwnerA, new AuthorizationVersion(1));
        var actor = new SimulatedCurrentUser(OwnerA, isAuthenticated: true);

        if (midFlight)
        {
            gate.BeforeAtomicCommit = () => gate.RevokeAndTransferOwnership(OwnerB);
        }
        else
        {
            gate.RevokeAndTransferOwnership(OwnerB);
        }

        var outcome = await gate.ExecuteValidatedSaveAsync(
            actor,
            new IsolatedRequirementMutationScope(ProjectId, OwnerA, Guid.NewGuid(), null),
            new AuthorizationVersion(1),
            F.Request(),
            CancellationToken.None);

        Assert.True(
            outcome.Status is IsolatedRequirementMutationStatus.AuthorizationChanged
                or IsolatedRequirementMutationStatus.AccessDenied,
            $"Expected AuthorizationChanged or AccessDenied, got {outcome.Status}.");
        Assert.Null(outcome.SavedRequirementRevisionId);
        Assert.Equal(0, gate.HistoryCount);
    }

    [Fact]
    public async Task StaleRequirementRevision_NullOrSupersededExpectedId_ConflictsWithoutOverwritingCurrent()
    {
        var gate = new SimulatedAtomicRequirementGate(ProjectId, OwnerA, new AuthorizationVersion(1));
        var actor = new SimulatedCurrentUser(OwnerA, isAuthenticated: true);

        var rev1 = await gate.ExecuteValidatedSaveAsync(
            actor,
            new IsolatedRequirementMutationScope(ProjectId, OwnerA, Guid.NewGuid(), null),
            new AuthorizationVersion(1),
            F.Request(),
            CancellationToken.None);
        var rev2 = await gate.ExecuteValidatedSaveAsync(
            actor,
            new IsolatedRequirementMutationScope(ProjectId, OwnerA, Guid.NewGuid(), rev1.SavedRequirementRevisionId),
            new AuthorizationVersion(1),
            F.Request(expected: rev1.SavedRequirementRevisionId),
            CancellationToken.None);

        Assert.Equal(IsolatedRequirementMutationStatus.Saved, rev2.Status);

        var nullAttempt = await gate.ExecuteValidatedSaveAsync(
            actor,
            new IsolatedRequirementMutationScope(ProjectId, OwnerA, Guid.NewGuid(), null),
            new AuthorizationVersion(1),
            F.Request(),
            CancellationToken.None);

        var staleAttempt = await gate.ExecuteValidatedSaveAsync(
            actor,
            new IsolatedRequirementMutationScope(ProjectId, OwnerA, Guid.NewGuid(), rev1.SavedRequirementRevisionId),
            new AuthorizationVersion(1),
            F.Request(expected: rev1.SavedRequirementRevisionId),
            CancellationToken.None);

        Assert.Equal(IsolatedRequirementMutationStatus.RequirementRevisionConflict, nullAttempt.Status);
        Assert.Null(nullAttempt.SavedRequirementRevisionId);
        Assert.Equal(IsolatedRequirementMutationStatus.RequirementRevisionConflict, staleAttempt.Status);
        Assert.Null(staleAttempt.SavedRequirementRevisionId);
        Assert.Equal(2, gate.HistoryCount);
        Assert.Equal(rev2.SavedRequirementRevisionId, gate.CurrentRevisionId);
    }

    [Fact]
    public async Task ConcurrentUpdates_SameExpectedRevision_AllowSingleWinnerAndRejectLoser()
    {
        var gate = new SimulatedAtomicRequirementGate(ProjectId, OwnerA, new AuthorizationVersion(1));
        var actor = new SimulatedCurrentUser(OwnerA, isAuthenticated: true);
        using var barrier = new Barrier(2);

        Task<IsolatedRequirementMutationOutcome> RunAttempt(string method) => Task.Run(async () =>
        {
            barrier.SignalAndWait();
            return await gate.ExecuteValidatedSaveAsync(
                actor,
                new IsolatedRequirementMutationScope(ProjectId, OwnerA, Guid.NewGuid(), null),
                new AuthorizationVersion(1),
                F.Request(method: method),
                CancellationToken.None);
        });

        var outcomes = await Task.WhenAll(RunAttempt("METHOD_A"), RunAttempt("METHOD_A"));

        Assert.Single(outcomes, o => o.Status == IsolatedRequirementMutationStatus.Saved);
        Assert.Single(outcomes, o => o.Status == IsolatedRequirementMutationStatus.RequirementRevisionConflict);
        var loser = outcomes.Single(o => o.Status == IsolatedRequirementMutationStatus.RequirementRevisionConflict);
        Assert.Null(loser.SavedRequirementRevisionId);
        Assert.Equal(1, gate.HistoryCount);
    }

    [Theory]
    [InlineData(SimulatedFailureMode.DependencyUnavailable, IsolatedRequirementMutationStatus.DependencyUnavailable, 0)]
    [InlineData(SimulatedFailureMode.DependencyTimeout, IsolatedRequirementMutationStatus.DependencyTimeout, 0)]
    [InlineData(SimulatedFailureMode.UncertainAfterRollback, IsolatedRequirementMutationStatus.CommitOutcomeUnknown, 0)]
    [InlineData(SimulatedFailureMode.UncertainAfterCommit, IsolatedRequirementMutationStatus.CommitOutcomeUnknown, 1)]
    public async Task TransactionFailureAndUncertainCommit_FailClosedWithoutDisclosureOrRetry(
        SimulatedFailureMode mode,
        IsolatedRequirementMutationStatus expectedStatus,
        int expectedHistoryCount)
    {
        var gate = new SimulatedAtomicRequirementGate(ProjectId, OwnerA, new AuthorizationVersion(1))
        {
            FailureMode = mode
        };
        var actor = new SimulatedCurrentUser(OwnerA, isAuthenticated: true);

        var outcome = await gate.ExecuteValidatedSaveAsync(
            actor,
            new IsolatedRequirementMutationScope(ProjectId, OwnerA, Guid.NewGuid(), null),
            new AuthorizationVersion(1),
            F.Request(),
            CancellationToken.None);

        Assert.Equal(expectedStatus, outcome.Status);
        Assert.Null(outcome.SavedRequirementRevisionId);
        Assert.Equal(expectedHistoryCount, gate.HistoryCount);
        Assert.Equal(1, gate.ExecutionAttempts);
    }

    [Fact]
    public async Task ProtectedPayloadNonDisclosure_DeniedOrConflictedOutcomesNeverExposeRevisionOrState()
    {
        var gate = new SimulatedAtomicRequirementGate(ProjectId, OwnerA, new AuthorizationVersion(1));
        var owner = new SimulatedCurrentUser(OwnerA, isAuthenticated: true);
        var saved = await gate.ExecuteValidatedSaveAsync(
            owner,
            new IsolatedRequirementMutationScope(ProjectId, OwnerA, Guid.NewGuid(), null),
            new AuthorizationVersion(1),
            F.Request(method: "METHOD_A"),
            CancellationToken.None);
        Assert.Equal(IsolatedRequirementMutationStatus.Saved, saved.Status);

        var attacker = new SimulatedCurrentUser(OwnerB, isAuthenticated: true);
        var crossOwnerOutcome = await gate.ExecuteValidatedSaveAsync(
            attacker,
            new IsolatedRequirementMutationScope(ProjectId, OwnerB, Guid.NewGuid(), null),
            new AuthorizationVersion(1),
            F.Request(),
            CancellationToken.None);

        // Cross-owner attempt must not reveal whether a revision already exists (AccessDenied, not RevisionConflict)
        Assert.Equal(IsolatedRequirementMutationStatus.AccessDenied, crossOwnerOutcome.Status);
        Assert.Null(crossOwnerOutcome.SavedRequirementRevisionId);
    }

    #endregion

    #region Simulated Test Harness (Not Production or DB Atomicity Proof)

    public enum SimulatedFailureMode
    {
        None,
        DependencyUnavailable,
        DependencyTimeout,
        UncertainAfterRollback,
        UncertainAfterCommit,
    }

    private sealed class SimulatedCurrentUser(Guid userId, bool isAuthenticated, Guid? requiredIdOverride = null) : ICurrentUser
    {
        public bool IsAuthenticated => isAuthenticated;
        public Guid? UserId => isAuthenticated ? userId : null;
        public IReadOnlySet<string> Roles { get; } = new HashSet<string>();
        public Guid GetRequiredUserId()
        {
            if (!isAuthenticated) throw new UnauthenticatedAccessException();
            return requiredIdOverride ?? userId;
        }
    }

    /// <summary>
    /// Simulated in-memory atomic gate combining M1/M3 authorization-version binding concepts
    /// with M5 requirement revision CAS semantics. Not a database implementation.
    /// </summary>
    private sealed class SimulatedAtomicRequirementGate(Guid projectId, Guid initialOwnerId, AuthorizationVersion initialVersion)
    {
        private readonly object _gate = new();
        private readonly List<RequirementRevision> _history = [];
        private Guid _currentOwnerId = initialOwnerId;
        private AuthorizationVersion _currentAuthVersion = initialVersion;
        private RequirementRevision? _currentRevision;

        public Action? BeforeAtomicCommit { get; set; }
        public SimulatedFailureMode FailureMode { get; set; } = SimulatedFailureMode.None;
        public int ExecutionAttempts { get; private set; }

        public int HistoryCount
        {
            get { lock (_gate) return _history.Count; }
        }

        public Guid? CurrentRevisionId
        {
            get { lock (_gate) return _currentRevision?.Id; }
        }

        public void RevokeAndTransferOwnership(Guid newOwnerId)
        {
            lock (_gate)
            {
                _currentOwnerId = newOwnerId;
                _currentAuthVersion = new AuthorizationVersion(_currentAuthVersion.Value + 1);
            }
        }

        public Task<IsolatedRequirementMutationOutcome> ExecuteValidatedSaveAsync(
            ICurrentUser user,
            IsolatedRequirementMutationScope scope,
            AuthorizationVersion expectedAuthVersion,
            SaveRequirementsRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecutionAttempts++;

            // 1. Trusted actor identity validation (fails closed)
            Guid actorId;
            try
            {
                if (!user.IsAuthenticated)
                    return Task.FromResult(new IsolatedRequirementMutationOutcome(IsolatedRequirementMutationStatus.AccessDenied));
                actorId = user.GetRequiredUserId();
                if (actorId == Guid.Empty || user.UserId != actorId || actorId != scope.TrustedActorId)
                    return Task.FromResult(new IsolatedRequirementMutationOutcome(IsolatedRequirementMutationStatus.AccessDenied));
            }
            catch (UnauthenticatedAccessException)
            {
                return Task.FromResult(new IsolatedRequirementMutationOutcome(IsolatedRequirementMutationStatus.AccessDenied));
            }

            // 2. Pre-transaction dependency checks
            if (FailureMode == SimulatedFailureMode.DependencyUnavailable)
                return Task.FromResult(new IsolatedRequirementMutationOutcome(IsolatedRequirementMutationStatus.DependencyUnavailable));
            if (FailureMode == SimulatedFailureMode.DependencyTimeout)
                return Task.FromResult(new IsolatedRequirementMutationOutcome(IsolatedRequirementMutationStatus.DependencyTimeout));

            var validation = RequirementContractValidator.Validate(request, F.Catalogue());
            if (validation.Status != RequirementValidationStatus.Valid)
                throw new InvalidOperationException("Test scenario requires a valid request payload.");

            // 3. Simulated TOCTOU window before atomic lock
            BeforeAtomicCommit?.Invoke();

            // 4. Simulated atomic commit section (in-memory only; does NOT prove DB atomicity)
            lock (_gate)
            {
                if (scope.ProjectId != projectId)
                    return Task.FromResult(new IsolatedRequirementMutationOutcome(IsolatedRequirementMutationStatus.ProjectNotFound));

                var binding = new AuthorizationBinding(projectId, actorId, expectedAuthVersion);
                var bindingStatus = AuthorizationBindingValidator.Validate(
                    binding, projectId, _currentOwnerId, _currentAuthVersion);

                if (bindingStatus == AuthorizationBindingValidationStatus.ActorMismatch)
                    return Task.FromResult(new IsolatedRequirementMutationOutcome(IsolatedRequirementMutationStatus.AccessDenied));
                if (bindingStatus != AuthorizationBindingValidationStatus.Valid)
                    return Task.FromResult(new IsolatedRequirementMutationOutcome(IsolatedRequirementMutationStatus.AuthorizationChanged));

                if (_currentRevision?.Id != scope.ExpectedRequirementRevisionId)
                    return Task.FromResult(new IsolatedRequirementMutationOutcome(IsolatedRequirementMutationStatus.RequirementRevisionConflict));

                if (FailureMode == SimulatedFailureMode.UncertainAfterRollback)
                    return Task.FromResult(new IsolatedRequirementMutationOutcome(IsolatedRequirementMutationStatus.CommitOutcomeUnknown));

                var nextNumber = _currentRevision is null ? 1L : checked(_currentRevision.RevisionNumber + 1);
                var newRevision = validation.CreateRevision(Guid.NewGuid(), projectId, nextNumber, DateTimeOffset.UtcNow);
                _currentRevision = newRevision;
                _history.Add(newRevision);

                if (FailureMode == SimulatedFailureMode.UncertainAfterCommit)
                    return Task.FromResult(new IsolatedRequirementMutationOutcome(IsolatedRequirementMutationStatus.CommitOutcomeUnknown));

                return Task.FromResult(new IsolatedRequirementMutationOutcome(
                    IsolatedRequirementMutationStatus.Saved,
                    newRevision.Id));
            }
        }
    }

    #endregion
}
