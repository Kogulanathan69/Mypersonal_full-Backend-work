using System.Text.Json;
using System.Text.Json.Serialization;
using BuildMind.API.Errors;
using BuildMind.Application.Features.Requirements.Abstractions;
using BuildMind.Application.Features.Requirements.Contracts;

namespace BuildMind.API.Endpoints.Requirements;

internal static class RequirementMutationHttp
{
    private static readonly JsonSerializerOptions Strict = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false
    };

    internal static async Task<IResult> PutAsync<T>(Guid project, HttpContext context, CancellationToken token)
    {
        var values = context.Request.Headers["Idempotency-Key"];
        if (values.Count != 1 || !TryId(values[0], out var operation))
            return RequirementsEndpointModule.Problem(400);
        object? body;
        try { body = await JsonSerializer.DeserializeAsync<T>(context.Request.Body, Strict, token); }
        catch (JsonException) { return RequirementsEndpointModule.Problem(400); }
        var errors = body switch
        {
            PutRequirementsHttpRequest full => full.GetStructuralErrors(),
            PutRequirementsSectionHttpRequest section => section.GetStructuralErrors(),
            _ => null
        };
        if (errors is null) return RequirementsEndpointModule.Problem(400);
        if (errors.Count > 0) return ApiProblems.ValidationFailed(errors);
        var mutation = body switch
        {
            PutRequirementsHttpRequest full => new RequirementMutation(RequirementMutationCategory.Full, full.ToApplicationRequest()),
            PutAdditionalSpacesHttpRequest spaces => new(RequirementMutationCategory.Spaces,
                new(spaces.ExpectedRevisionId, spaces.CatalogueVersion, null, null, additionalSpaces: spaces.ToApplicationRequest().AdditionalSpaces)),
            PutRoomRelationshipsHttpRequest relationships => new(RequirementMutationCategory.Relationships,
                new(relationships.ExpectedRevisionId, relationships.CatalogueVersion, null, null, relationships: relationships.ToApplicationRequest().Relationships)),
            PutDesignMethodHttpRequest method => new(RequirementMutationCategory.DesignMethod,
                new(method.ExpectedRevisionId, method.CatalogueVersion, null, null, designMethod: method.DesignMethod)),
            _ => throw new InvalidOperationException()
        };
        return await ExecuteAsync(context.RequestServices, project, operation, mutation, token);
    }

    internal static async Task<IResult> ObserveAsync(string id, string operationId, HttpContext context, CancellationToken token)
    {
        context.Response.Headers.CacheControl = "no-store";
        if (!Guid.TryParse(id, out var project) || project == Guid.Empty) return RequirementsEndpointModule.InvalidProject();
        if (!TryId(operationId, out var operation)) return RequirementsEndpointModule.Problem(400);
        await using var scope = context.RequestServices.CreateAsyncScope();
        return await ExecuteAsync(scope.ServiceProvider, project, operation, null, token);
    }

    private static bool TryId(string? text, out Guid id)
    {
        id = default;
        return text?.Length == 36 && Guid.TryParseExact(text, "D", out id) && id != Guid.Empty;
    }

    private static async Task<IResult> ExecuteAsync(IServiceProvider services, Guid project, Guid operation,
        RequirementMutation? mutation, CancellationToken token)
    {
        var coordinator = services.GetService<IRequirementMutationCoordinator>();
        if (coordinator is null) return RequirementsEndpointModule.Problem(503);
        var result = await coordinator.ExecuteAsync(project, operation, mutation, token);
        return result.Status switch
        {
            RequirementMutationStatus.Committed when result.Revision?.ProjectId == project => mutation is null
                ? Results.Ok(new { status = "committed", operationId = operation, revisionId = result.Revision.RevisionId })
                : Results.Ok(result.Revision),
            RequirementMutationStatus.Absent when mutation is null => Results.Ok(new { status = "absentAtObservation", operationId = operation }),
            RequirementMutationStatus.Denied => RequirementsEndpointModule.Problem(404),
            RequirementMutationStatus.Reused => RequirementsEndpointModule.Problem(409, "IDEMPOTENCY_KEY_REUSED"),
            RequirementMutationStatus.Unknown => RequirementsEndpointModule.Problem(503, "REQUIREMENT_COMMIT_OUTCOME_UNKNOWN"),
            RequirementMutationStatus.Failed when result.Failure is not null => RequirementsEndpointModule.Map(result.Failure, project, save: true),
            _ => RequirementsEndpointModule.Problem(500)
        };
    }
}
