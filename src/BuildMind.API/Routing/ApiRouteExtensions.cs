namespace BuildMind.API.Routing;

/// <summary>Registers the public product API route group (decision C1).</summary>
public static class ApiRouteExtensions
{
    /// <summary>Approved public product API prefix.</summary>
    public const string ApiV1Prefix = "/api/v1";

    /// <summary>
    /// Creates the <c>/api/v1</c> route group and maps every registered <see cref="IApiEndpointModule"/>
    /// onto it. With no modules registered the group maps no endpoints.
    /// </summary>
    public static RouteGroupBuilder MapBuildMindApiV1(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var apiV1 = app.MapGroup(ApiV1Prefix);

        foreach (var module in app.Services.GetServices<IApiEndpointModule>())
        {
            module.MapEndpoints(apiV1);
        }

        return apiV1;
    }
}
