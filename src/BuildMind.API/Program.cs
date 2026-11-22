using BuildMind.API;
using BuildMind.API.Documentation;
using BuildMind.API.Endpoints.Regulations;
using BuildMind.API.Health;
using BuildMind.API.Routing;
using BuildMind.API.Security;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using BuildMind.Application;
using BuildMind.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Composition root: the API project is the only place that wires layers together.
builder.Services
    .AddApplication()
    .AddInfrastructure(builder.Configuration)
    .AddBuildMindApiInfrastructure(builder.Configuration);

// C6 (proposed): exact-origin CORS allowlist from configuration. No origins configured => no CORS policy at all.
var corsEnabled = builder.Services.AddBuildMindCors(builder.Configuration, builder.Environment);

// M1 end-user authentication (JWT bearer). Off unless Authentication:Enabled; activation outside Development is refused
// at start-up until approved. Returns whether authentication middleware must run.
var m1AuthEnabled = builder.AddM1AuthenticationApi();

// M5 customer dashboard: its production dependencies (ICurrentUser from JWT, IProjectRepository from M1) exist only
// when M1 authentication is active, so the module is never registered without them.
if (m1AuthEnabled)
{
    builder.Services.AddDashboardFeature();
}

// M7 public regulation evidence retrieval: OFF unless Regulations:Retrieval:Enabled (lead contract approval pending).
// Fails closed: when enabled it requires authentication and the internal RAG service.
builder.Services.AddRegulationRetrievalFeature(builder.Configuration, m1AuthEnabled);

// C7 (proposed): OpenAPI document and Swagger UI, Development only. Bearer scheme only when authentication is enabled.
builder.AddBuildMindDevelopmentSwagger(bearerAuthenticationEnabled: m1AuthEnabled);

// S4-B: rate limiting (global chained limiter + auth-strict policy + expensive-operation concurrency).
builder.Services.AddBuildMindRateLimiting(builder.Configuration);

var app = builder.Build();

// D04: safe response headers first, so they are also applied to error responses.
app.UseMiddleware<SecurityHeadersMiddleware>();

// C3: safe RFC 9457 responses for unhandled exceptions and for empty 4xx/5xx responses (e.g. unknown routes).
app.UseExceptionHandler();
app.UseStatusCodePages();

// Development only; a no-op in every other environment.
app.UseBuildMindDevelopmentSwagger();

// Order: routing -> CORS -> authentication -> rate limiting -> account-state check -> authorization.
// CORS answers preflights before authentication; authentication runs BEFORE the rate limiter so authenticated callers
// get per-user partitions; authorization runs last. Feature modules must not call UseRateLimiter() themselves.
app.UseRouting();
if (corsEnabled)
{
    app.UseCors(CorsConfiguration.PolicyName);
}

if (m1AuthEnabled)
{
    app.UseAuthentication();
}

app.UseRateLimiter();

if (m1AuthEnabled)
{
    // Per-request account-state check (disabled accounts, revoked token versions; fail closed). After the rate limiter,
    // so floods are throttled before they reach the database; before authorization, so revoked sessions never authorize.
    app.UseMiddleware<ActiveSessionMiddleware>();
    app.UseAuthorization();
}

// Liveness only: reports that the API process is running. It does not check MySQL, Python or RAG.
app.MapGet("/health", () => Results.Ok(new { service = "buildmind-api", status = "ok" }));

// D04 readiness (C11b): MySQL mandatory (503 when not configured/unreachable); Python/RAG Degraded until D05.
app.MapHealthChecks(Readiness.Path, new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains(Readiness.Tag),
    ResponseWriter = Readiness.WriteResponseAsync,
    ResultStatusCodes = new Dictionary<Microsoft.Extensions.Diagnostics.HealthChecks.HealthStatus, int>(Readiness.StatusCodes),
    AllowCachingResponses = false,
});

// C1: public product API under /api/v1; feature modules register IApiEndpointModule implementations.
app.MapBuildMindApiV1();

app.Run();

/// <summary>Exposed so integration tests can host the API with WebApplicationFactory.</summary>
public partial class Program
{
}
