namespace BuildMind.API.Routing;

/// <summary>
/// A feature module's public product endpoints, mapped under the versioned <c>/api/v1</c> group
/// (decision C1).
/// </summary>
/// <remarks>
/// Feature owners register implementations in DI so new modules do not need to edit
/// <c>Program.cs</c> (single-writer file, M4 custodian). Operational endpoints such as <c>/health</c>
/// stay unversioned and are not modules. Internal Python/RAG routes are never exposed here.
/// </remarks>
public interface IApiEndpointModule
{
    void MapEndpoints(RouteGroupBuilder apiV1);
}
