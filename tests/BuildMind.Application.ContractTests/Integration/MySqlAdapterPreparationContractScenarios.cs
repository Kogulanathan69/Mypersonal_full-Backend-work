using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Features.PlanEditor.Editing;
using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.PlanEditor.Versioning;

namespace BuildMind.Application.ContractTests.Integration;

/// <summary>
/// Database-free STEP 15-B checks for the exact M3 seams offered to M4. These scenarios prove application
/// contract shape and orchestration only. They do not prove SQL predicates, constraints, isolation, locking,
/// durability, rollback, commit-unknown reconciliation, or MySQL behavior.
/// </summary>
internal static class MySqlAdapterPreparationContractScenarios
{
    public static void Register(Action<string, Action> run)
    {
        run("MYSQL-PREP-01 exact M3 persistence port surfaces remain cancellation-aware", () =>
        {
            var expected = new Dictionary<Type, string[]>
            {
                [typeof(IPlanRevisionHistoryQueryPort)] = ["ReadPageAsync", "ReadRevisionAsync"],
                [typeof(IPlanRevisionStore)] = ["GetAsync", "GetCurrentAsync", "GetHistoryAsync", "TryAppendAtomicallyAsync"],
                [typeof(ICandidatePreviewStore)] = ["CreateAsync", "GetAsync"],
                [typeof(ICandidatePreviewTransaction)] = ["TryApplyAtomicallyAsync", "TryExpireAtomicallyAsync", "TryRejectAtomicallyAsync"],
                [typeof(IAtomicPlanMutationPort)] =
                [
                    "AppendPlanRevisionAsync", "ApplyCandidateAsync", "ConfirmFinalPlanAsync", "CreateCandidateAsync",
                    "ExpireCandidateAsUserAsync", "ReconcileAsync", "RejectCandidateAsync"
                ],
                [typeof(IProjectAuthorizationPort)] = ["AuthorizeOwnerAsync"],
            };

            foreach (var (port, names) in expected)
            {
                var methods = port.GetMethods().OrderBy(method => method.Name, StringComparer.Ordinal).ToArray();
                Check(methods.Select(method => method.Name).SequenceEqual(names),
                    $"{port.Name} method inventory changed without updating the M4 handoff.");

                foreach (var method in methods)
                {
                    var parameters = method.GetParameters();
                    Check(parameters.Length > 0 && parameters[^1].ParameterType == typeof(CancellationToken),
                        $"{port.Name}.{method.Name} must end with CancellationToken.");
                    Check(IsApplicationOrSystemType(method.ReturnType),
                        $"{port.Name}.{method.Name} exposed a persistence-provider return type.");
                    Check(parameters.All(parameter => IsApplicationOrSystemType(parameter.ParameterType)),
                        $"{port.Name}.{method.Name} exposed a persistence-provider parameter type.");
                }
            }
        });

        run("MYSQL-PREP-02 bounded history seam preserves exact scope cursor limit and cancellation", () =>
        {
            var projectId = Guid.NewGuid();
            var planId = Guid.NewGuid();
            var actorId = Guid.NewGuid();
            var headRevisionId = Guid.NewGuid();
            var afterRevisionId = Guid.NewGuid();
            var expectedNextRevisionId = Guid.NewGuid();
            var cursor = new PlanRevisionHistoryCursor(
                projectId, planId, 3, headRevisionId, 2, afterRevisionId, expectedNextRevisionId);
            var authorization = new AuthorizationProbe(projectId, actorId, new AuthorizationVersion(7));
            var history = new HistoryProbe();
            var service = new PlanRevisionHistoryService(
                new CurrentUserProbe(actorId), authorization, history);
            using var cancellation = new CancellationTokenSource();

            var result = service.ListAsync(
                new PlanRevisionHistoryQuery(projectId, planId, 7, cursor), cancellation.Token).Result;

            Check(result.Status == PlanRevisionHistoryStatus.InvalidCursor,
                "The adapter's invalid-cursor result was not preserved.");
            Check(result.Revisions is null && result.NextCursor is null,
                "An invalid cursor exposed protected revision data.");
            Check(authorization.Calls == 2 && authorization.LastProjectId == projectId &&
                  authorization.LastActorId == actorId && authorization.LastToken == cancellation.Token,
                "Authorization was not checked with the exact actor, project and caller token around the read.");
            Check(history.PageCalls == 1 && history.RevisionCalls == 0 &&
                  history.LastProjectId == projectId && history.LastPlanId == planId &&
                  history.LastPageSize == 7 && history.LastCursor == cursor &&
                  history.LastToken == cancellation.Token,
                "The bounded adapter seam did not receive the exact scoped continuation request.");
        });

        run("MYSQL-PREP-03 legacy stores do not claim authorization fingerprint or reconciliation guarantees", () =>
        {
            var unboundedHistory = RequireMethod(typeof(IPlanRevisionStore), "GetHistoryAsync");
            Check(unboundedHistory.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(
                    [typeof(Guid), typeof(Guid), typeof(CancellationToken)]),
                "The legacy history surface changed; reassess bounded-read routing.");

            var candidateLookup = RequireMethod(typeof(ICandidatePreviewStore), "GetAsync");
            Check(candidateLookup.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(
                    [typeof(Guid), typeof(CancellationToken)]),
                "The candidate lookup surface changed; reassess scope enforcement.");

            var legacyPorts = new[]
            {
                typeof(IPlanRevisionStore), typeof(ICandidatePreviewStore), typeof(ICandidatePreviewTransaction)
            };
            foreach (var port in legacyPorts)
            {
                Check(port.GetMethod("ReconcileAsync") is null,
                    $"{port.Name} unexpectedly acquired reconciliation semantics.");
                Check(port.GetMethods().SelectMany(method => method.GetParameters()).All(parameter =>
                        parameter.ParameterType != typeof(AuthorizationVersion) &&
                        parameter.ParameterType != typeof(MutationIdentity) &&
                        parameter.ParameterType != typeof(AtomicMutationReconciliationRequest)),
                    $"{port.Name} authorization or mutation identity surface changed; joint review is required.");
            }
        });

        run("MYSQL-PREP-04 atomic mutation seam exposes six typed mutations and reconciliation", () =>
        {
            var mutationMethods = typeof(IAtomicPlanMutationPort).GetMethods()
                .Where(method => method.Name != "ReconcileAsync")
                .OrderBy(method => method.Name, StringComparer.Ordinal)
                .ToArray();
            Check(mutationMethods.Length == 6, "The atomic port no longer exposes exactly six mutation categories.");

            foreach (var method in mutationMethods)
            {
                var requestType = method.GetParameters()[0].ParameterType;
                Check(requestType.GetProperty("Scope")?.PropertyType == typeof(AtomicMutationScope),
                    $"{requestType.Name} lost its exact mutation scope.");
                Check(requestType.GetProperty("CurrentAuthorization")?.PropertyType == typeof(AuthorizationBinding),
                    $"{requestType.Name} lost its authorization binding.");
                Check(requestType.GetProperty("Mutation")?.PropertyType == typeof(MutationIdentity),
                    $"{requestType.Name} lost its durable mutation identity.");
            }

            var reconcile = RequireMethod(typeof(IAtomicPlanMutationPort), "ReconcileAsync");
            Check(reconcile.GetParameters().Select(parameter => parameter.ParameterType).SequenceEqual(
                    [typeof(AtomicMutationReconciliationRequest), typeof(CancellationToken)]),
                "Reconciliation no longer requires the exact reference and caller cancellation.");
            Check(reconcile.ReturnType == typeof(Task<ReconciliationResult>),
                "Reconciliation return contract changed.");
        });
    }

    private static System.Reflection.MethodInfo RequireMethod(Type port, string name) =>
        port.GetMethod(name) ?? throw new InvalidOperationException($"{port.Name}.{name} is missing.");

    private static bool IsApplicationOrSystemType(Type type)
    {
        if (type.IsArray)
            return IsApplicationOrSystemType(type.GetElementType()!);

        var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        var assembly = definition.Assembly.GetName().Name ?? string.Empty;
        var allowed = assembly == typeof(IPlanRevisionStore).Assembly.GetName().Name ||
                      assembly is "System.Private.CoreLib" or "System.Runtime" or "System.Threading" or "System.Threading.Tasks";
        return allowed && (!type.IsGenericType || type.GetGenericArguments().All(IsApplicationOrSystemType));
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class CurrentUserProbe(Guid actorId) : ICurrentUser
    {
        public bool IsAuthenticated => true;
        public Guid? UserId => actorId;
        public IReadOnlySet<string> Roles { get; } = new HashSet<string>();
        public Guid GetRequiredUserId() => actorId;
    }

    private sealed class AuthorizationProbe(
        Guid expectedProjectId,
        Guid expectedActorId,
        AuthorizationVersion version) : IProjectAuthorizationPort
    {
        public int Calls { get; private set; }
        public Guid LastProjectId { get; private set; }
        public Guid LastActorId { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public Task<ProjectAuthorizationResult> AuthorizeOwnerAsync(
            Guid projectId,
            Guid authenticatedActorId,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastProjectId = projectId;
            LastActorId = authenticatedActorId;
            LastToken = cancellationToken;
            Check(projectId == expectedProjectId && authenticatedActorId == expectedActorId,
                "Authorization probe received a substituted identity.");
            return Task.FromResult(new ProjectAuthorizationResult(
                ProjectAuthorizationStatus.Authorized,
                new ProjectAuthorizationSnapshot(projectId, authenticatedActorId, version)));
        }
    }

    private sealed class HistoryProbe : IPlanRevisionHistoryQueryPort
    {
        public int PageCalls { get; private set; }
        public int RevisionCalls { get; private set; }
        public Guid LastProjectId { get; private set; }
        public Guid LastPlanId { get; private set; }
        public int LastPageSize { get; private set; }
        public PlanRevisionHistoryCursor? LastCursor { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public Task<PlanRevisionHistorySourcePage> ReadPageAsync(
            Guid projectId,
            Guid planId,
            PlanRevisionHistoryCursor? cursor,
            int pageSize,
            CancellationToken cancellationToken)
        {
            PageCalls++;
            LastProjectId = projectId;
            LastPlanId = planId;
            LastCursor = cursor;
            LastPageSize = pageSize;
            LastToken = cancellationToken;
            return Task.FromResult(new PlanRevisionHistorySourcePage(
                PlanRevisionHistorySourceStatus.InvalidCursor));
        }

        public Task<PlanRevisionHistorySourceRevision> ReadRevisionAsync(
            Guid projectId,
            Guid planId,
            Guid revisionId,
            CancellationToken cancellationToken)
        {
            RevisionCalls++;
            return Task.FromResult(new PlanRevisionHistorySourceRevision(
                PlanRevisionHistorySourceStatus.NotFound));
        }
    }
}
