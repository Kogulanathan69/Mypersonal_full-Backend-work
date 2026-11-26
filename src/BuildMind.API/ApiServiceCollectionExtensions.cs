using BuildMind.API.Configuration;
using BuildMind.API.Errors;
using BuildMind.API.Health;
using BuildMind.Infrastructure.Integration;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using BuildMind.API.Security;
using BuildMind.Application.Abstractions;
using BuildMind.Infrastructure.Configuration;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace BuildMind.API;

/// <summary>API-layer shared infrastructure registration (BE-M4-D01c), called by the composition root.</summary>
public static class ApiServiceCollectionExtensions
{
    public static IServiceCollection AddBuildMindApiInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // C3/C5: RFC 9457 Problem Details with BuildMind code, type and traceId conventions.
        services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsDefaults.Apply);

        // M6-review correction: a JSON body with duplicate properties is malformed (400), never "last one wins".
        services.ConfigureHttpJsonOptions(options => options.SerializerOptions.AllowDuplicateProperties = false);
        services.AddExceptionHandler<GlobalExceptionHandler>();

        // C10: the framework middleware's own exception diagnostics (which include the full exception) are
        // suppressed only when GlobalExceptionHandler handled the exception — it logs safe metadata itself.
        // Framework diagnostics stay on for handler failures and other unhandled paths (no blanket log filter).
        services.Configure<ExceptionHandlerOptions>(options =>
            options.SuppressDiagnosticsCallback = ShouldSuppressFrameworkDiagnostics);

        // Current user derived from the authenticated principal only.
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpContextCurrentUser>();

        // C8: typed internal-service destinations, validated at start-up. Disabled services never block start-up.
        services.AddOptions<ServicesOptions>()
            .Bind(configuration.GetSection(ServicesOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<ServicesOptions>, ServicesOptionsValidation>();

        // BE-M4-D02a: database settings, validated at start-up only when Database:Enabled is true.
        // The connection string comes from ConnectionStrings:BuildMind (User Secrets / environment variables).
        services.AddOptions<DatabaseOptions>()
            .Bind(configuration.GetSection(DatabaseOptions.SectionName))
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<DatabaseOptions>, DatabaseOptionsValidation>();

        // STEP 05 H1–H4: host-header filtering uses the framework middleware (AllowedHosts). Outside Development the
        // list must contain exact host names only; validated at start-up so a missing or "*" value cannot ship.
        services.AddOptions<Microsoft.AspNetCore.HostFiltering.HostFilteringOptions>().ValidateOnStart();
        services.AddSingleton<
            IValidateOptions<Microsoft.AspNetCore.HostFiltering.HostFilteringOptions>, AllowedHostsValidation>();

        // BE-M4-D04 readiness (C11b): MySQL mandatory; Python/RAG instance-optional (never Healthy before D05).
        services.AddSingleton<ReadinessProbeGate>();
        services.AddHealthChecks()
            .AddCheck<MySqlReadinessCheck>(
                "mysql", HealthStatus.Unhealthy, [Readiness.Tag], Readiness.RegistrationTimeout)
            .AddCheck<PythonReadinessCheck>(
                "python", HealthStatus.Degraded, [Readiness.Tag], Readiness.RegistrationTimeout)
            .AddCheck<RagReadinessCheck>(
                "rag", HealthStatus.Degraded, [Readiness.Tag], Readiness.RegistrationTimeout);

        // BE-M4-D05: hardened typed client for the private Python service (only confirmed routes; G11 auth open).
        services.AddSingleton<InternalServiceBulkhead>(); // STEP 08-C: bounded in-flight + queued calls per dependency
        services.AddHttpClient<PythonServiceClient>()
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan) // the client enforces TimeoutSeconds itself
            .ConfigurePrimaryHttpMessageHandler(CreateInternalServiceHandler)
            .RemoveAllLoggers(); // framework request logs would record internal URLs

        // BE-M7 (evidence contract v1, lead-approved 2026-11-19): private regulation retrieval service, same hardened
        // transport and bulkhead. Exposed to the Application layer only through IRegulationEvidenceSource.
        services.AddHttpClient<RagServiceClient>()
            .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(CreateInternalServiceHandler)
            .RemoveAllLoggers();
        services.AddTransient<BuildMind.Application.Abstractions.Regulations.IRegulationEvidenceSource>(
            provider => provider.GetRequiredService<RagServiceClient>());
        services.AddTransient<BuildMind.Application.Abstractions.Regulations.IRegulatoryConstraintSource>(
            provider => provider.GetRequiredService<RagServiceClient>());

        // BE-M4-D04: do not advertise the server implementation. This only affects Kestrel; a reverse proxy
        // or load balancer in front of the API may add its own headers and must be configured separately.
        services.Configure<KestrelServerOptions>(options => options.AddServerHeader = false);

        // STEP 05 S4-A: global request-body limit. The host does NOT bind Kestrel:Limits from configuration (verified
        // 2026-11-21: Kestrel kept its 30,000,000-byte default), so the value is applied here explicitly: 1 MiB when the
        // key is absent; outside Development at most 8 MiB, validated at start-up.
        services.Configure<KestrelServerOptions>(options =>
            options.Limits.MaxRequestBodySize = RequestBodyLimitValidation.ReadConfiguredLimit(configuration));
        services.AddOptions<KestrelServerOptions>().ValidateOnStart();
        services.AddSingleton<IValidateOptions<KestrelServerOptions>, RequestBodyLimitValidation>();

        return services;
    }

    /// <summary>Upper bound on concurrent connections to one internal service (requests beyond it queue).</summary>
    public const int MaxInternalConnectionsPerServer = 32;

    /// <summary>
    /// Transport for private internal services (BE-M4-D05): never follows redirects (a redirect could point
    /// anywhere), never stores or sends cookies, no automatic decompression, bounded connect time and pooled
    /// connections recycled so DNS changes are picked up.
    /// </summary>
    public static HttpMessageHandler CreateInternalServiceHandler() => new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        // STEP 08-B: never route private traffic through HTTP(S)_PROXY / ALL_PROXY or a system proxy.
        UseProxy = false,
        // STEP 08-B: bound concurrent connections, so a slow dependency cannot accumulate unbounded outstanding work.
        MaxConnectionsPerServer = MaxInternalConnectionsPerServer,
        AutomaticDecompression = System.Net.DecompressionMethods.None,
        ConnectTimeout = TimeSpan.FromSeconds(5),
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        MaxResponseHeadersLength = 64, // KiB
    };

    /// <summary>True only when a registered <c>IExceptionHandler</c> service handled the exception.</summary>
    public static bool ShouldSuppressFrameworkDiagnostics(ExceptionHandlerSuppressDiagnosticsContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.ExceptionHandledBy == ExceptionHandledType.ExceptionHandlerService;
    }
}
