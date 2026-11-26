using System.Text;
using BuildMind.API.Endpoints.Requirements;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace BuildMind.Infrastructure.Tests;

public sealed class RequirementMutationHttpTests
{
    private const string Key = "00000000-0000-4000-8000-000000000001";
    private const string Body = """{"expectedRevisionId":"00000000-0000-4000-8000-000000000002","catalogueVersion":"v","designMethod":null}""";

    [Theory]
    [InlineData(",\"designMethod\":null")]
    [InlineData(",\"extra\":true")]
    [InlineData(",\"DesignMethod\":null")]
    public async Task Ambiguous_keyed_json_is_rejected_before_dispatch(string member)
    {
        await AssertRejected(Body[..^1] + member + "}", Key);
    }

    [Fact]
    public async Task Missing_member_and_invalid_unicode_are_rejected()
    {
        await AssertRejected(Body.Replace(",\"designMethod\":null", ""), Key);
        await AssertRejected(Body.Replace("null", "\"\\uD800\""), Key);
    }

    [Theory]
    [InlineData("")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    [InlineData(Key + "," + Key)]
    public Task Invalid_key_is_rejected(string key) => AssertRejected(Body, key);

    private static async Task AssertRejected(string body, string key)
    {
        var context = Context(body, key);
        var result = await RequirementMutationHttp.PutAsync<PutDesignMethodHttpRequest>(Guid.NewGuid(), context, default);
        Assert.Equal(400, Assert.IsAssignableFrom<IStatusCodeHttpResult>(result).StatusCode);
    }

    [Theory]
    [InlineData(RequirementMutationStatus.Unknown, 503, "REQUIREMENT_COMMIT_OUTCOME_UNKNOWN")]
    [InlineData(RequirementMutationStatus.Reused, 409, "IDEMPOTENCY_KEY_REUSED")]
    [InlineData(RequirementMutationStatus.Denied, 404, "NOT_FOUND")]
    public async Task Keyed_decision_maps_to_safe_problem_once(RequirementMutationStatus decision, int status, string code)
    {
        var coordinator = new Coordinator(decision);
        using var services = new ServiceCollection().AddSingleton<IRequirementMutationCoordinator>(coordinator).BuildServiceProvider();
        var context = Context(Body, Key);
        context.RequestServices = services;
        var result = await RequirementMutationHttp.PutAsync<PutDesignMethodHttpRequest>(Guid.NewGuid(), context, default);
        var problem = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.ProblemHttpResult>(result).ProblemDetails;
        Assert.Equal(status, problem.Status);
        Assert.Equal(code, problem.Extensions["code"]);
        Assert.Equal(1, coordinator.Calls);
    }

    private static DefaultHttpContext Context(string body, string key)
    {
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
        context.Request.Headers["Idempotency-Key"] = key;
        return context;
    }

    private sealed class Coordinator(RequirementMutationStatus status) : IRequirementMutationCoordinator
    {
        public int Calls { get; private set; }
        public Task<RequirementMutationResult> ExecuteAsync(Guid projectId, Guid operationId, RequirementMutation? mutation, CancellationToken token)
        {
            Calls++;
            Assert.Equal(Guid.Parse(Key), operationId);
            Assert.Equal(RequirementMutationCategory.DesignMethod, mutation!.Category);
            return Task.FromResult(new RequirementMutationResult(status));
        }
    }
}
