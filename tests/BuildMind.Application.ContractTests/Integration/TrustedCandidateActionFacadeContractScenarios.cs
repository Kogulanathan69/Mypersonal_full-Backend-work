using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Features.PlanEditor.Editing;

namespace BuildMind.Application.ContractTests.Integration;

internal static class TrustedCandidateActionFacadeContractScenarios
{
    public static void Register(Action<string, Action> run)
    {
        run("trusted candidate actions deny unauthenticated apply and reject", () =>
        {
            foreach (Guid? actor in new Guid?[] { null, Guid.Empty })
            {
                var currentUser = new CurrentUserDouble(actor);
                var facade = CreateFacade(currentUser);

                var apply = facade.ApplyAsync(
                    new ApplyCandidateCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true))
                    .GetAwaiter().GetResult();

                var reject = facade.RejectAsync(
                    new CandidateActionCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()))
                    .GetAwaiter().GetResult();

                Check(apply.Status == CandidateActionStatus.Unauthorized,
                    "Unauthenticated apply was not denied.");
                Check(reject.Status == CandidateActionStatus.Unauthorized,
                    "Unauthenticated reject was not denied.");
                Check(apply.Candidate is null && apply.Revision is null &&
                      reject.Candidate is null && reject.Revision is null,
                    "Authentication denial disclosed protected data.");
                Check(currentUser.RequiredUserCalls == 2,
                    "Trusted identity was not resolved exactly once per action.");
            }
        });

        run("trusted candidate actions reject pre-cancelled apply and reject", () =>
        {
            var currentUser = new CurrentUserDouble(Guid.NewGuid());
            var facade = CreateFacade(currentUser);

            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            CheckThrows<OperationCanceledException>(() =>
                facade.ApplyAsync(
                    new ApplyCandidateCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), true),
                    cancellation.Token).GetAwaiter().GetResult());

            CheckThrows<OperationCanceledException>(() =>
                facade.RejectAsync(
                    new CandidateActionCommand(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()),
                    cancellation.Token).GetAwaiter().GetResult());

            Check(currentUser.RequiredUserCalls == 0,
                "Pre-cancelled action resolved identity or reached protected work.");
        });
    }

    private static TrustedCandidateActionFacade CreateFacade(ICurrentUser currentUser)
    {
        // An uninitialized service is safe only in these short-circuit tests.
        // Any accidental delegation must fail the test instead of mutating state.
        var service = (CandidatePreviewService)
            System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(
                typeof(CandidatePreviewService));

        return new TrustedCandidateActionFacade(currentUser, service);
    }

    private sealed class CurrentUserDouble(Guid? userId) : ICurrentUser
    {
        public bool IsAuthenticated => userId is not null && userId != Guid.Empty;
        public Guid? UserId => IsAuthenticated ? userId : null;
        public IReadOnlySet<string> Roles { get; } = new HashSet<string>();
        public int RequiredUserCalls { get; private set; }

        public Guid GetRequiredUserId()
        {
            RequiredUserCalls++;

            if (!IsAuthenticated)
                throw new UnauthenticatedAccessException();

            return userId!.Value;
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition)
            throw new Exception(message);
    }

    private static void CheckThrows<T>(Action action) where T : Exception
    {
        try
        {
            action();
        }
        catch (T)
        {
            return;
        }

        throw new Exception($"Expected {typeof(T).Name}.");
    }
}