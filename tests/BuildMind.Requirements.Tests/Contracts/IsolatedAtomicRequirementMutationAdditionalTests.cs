using BuildMind.Application.Features.Requirements.Integration;

namespace BuildMind.Requirements.Tests.Contracts;

public sealed class IsolatedAtomicRequirementMutationAdditionalTests
{
    [Theory]
    [InlineData(IsolatedRequirementMutationStatus.AccessDenied)]
    [InlineData(IsolatedRequirementMutationStatus.AuthorizationChanged)]
    [InlineData(IsolatedRequirementMutationStatus.RequirementRevisionConflict)]
    [InlineData(IsolatedRequirementMutationStatus.ProjectNotFound)]
    [InlineData(IsolatedRequirementMutationStatus.DependencyUnavailable)]
    [InlineData(IsolatedRequirementMutationStatus.DependencyTimeout)]
    [InlineData(IsolatedRequirementMutationStatus.CommitOutcomeUnknown)]
    public void NonSuccessOutcome_PreservesExactClassification(
        IsolatedRequirementMutationStatus status)
    {
        var outcome = new IsolatedRequirementMutationOutcome(status);

        Assert.Equal(status, outcome.Status);
        Assert.Null(outcome.SavedRequirementRevisionId);
    }

    [Fact]
    public void Scope_IdentityPropertiesHaveNoSetters()
    {
        var type = typeof(IsolatedRequirementMutationScope);

        foreach (var propertyName in new[]
        {
            nameof(IsolatedRequirementMutationScope.ProjectId),
            nameof(IsolatedRequirementMutationScope.TrustedActorId),
            nameof(IsolatedRequirementMutationScope.OperationId),
            nameof(IsolatedRequirementMutationScope.ExpectedRequirementRevisionId)
        })
        {
            var property = type.GetProperty(propertyName);
            Assert.NotNull(property);
            Assert.Null(property!.SetMethod);
        }
    }

    [Fact]
    public void Outcome_PublicPropertiesHaveNoSetters()
    {
        var type = typeof(IsolatedRequirementMutationOutcome);

        foreach (var propertyName in new[]
        {
            nameof(IsolatedRequirementMutationOutcome.Status),
            nameof(IsolatedRequirementMutationOutcome.SavedRequirementRevisionId)
        })
        {
            var property = type.GetProperty(propertyName);
            Assert.NotNull(property);
            Assert.Null(property!.SetMethod);
        }
    }

    [Fact]
    public void Port_RequiresScopeAndCancellationToken()
    {
        var method = typeof(IIsolatedAtomicRequirementMutationPort)
            .GetMethod("ExecuteAsync");

        Assert.NotNull(method);

        Assert.Equal(
            typeof(Task<IsolatedRequirementMutationOutcome>),
            method!.ReturnType);

        var parameters = method.GetParameters();

        Assert.Equal(2, parameters.Length);
        Assert.Equal(
            typeof(IsolatedRequirementMutationScope),
            parameters[0].ParameterType);
        Assert.Equal(
            typeof(CancellationToken),
            parameters[1].ParameterType);
    }

    [Fact]
    public void InvalidNegativeOutcomeStatus_IsRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new IsolatedRequirementMutationOutcome(
                (IsolatedRequirementMutationStatus)(-1)));
    }
}
