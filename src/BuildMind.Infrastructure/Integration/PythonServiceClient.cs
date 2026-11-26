using BuildMind.Infrastructure.Configuration;
using Microsoft.Extensions.Options;

namespace BuildMind.Infrastructure.Integration;

/// <summary>
/// Typed client for the private Python FastAPI service (BE-M4-D05). Only routes that exist in the repository are
/// exposed: <c>GET /health</c> (<c>python-services/app/main.py</c>). Feature routes (survey, land, generation,
/// validation, plan editor) are owned by M2/M3/M6 and are added here only once their contracts exist; nothing is
/// invented.
/// </summary>
public sealed class PythonServiceClient(HttpClient http, IOptions<ServicesOptions> options, InternalServiceBulkhead bulkhead)
    : InternalServiceClient(http, options.Value.Python, DependencyName, DefaultMaxResponseBytes, bulkhead)
{
    public const string DependencyName = "python";

    /// <summary>Confirmed liveness contract: <c>{"service":"buildmind-python","status":"ok"}</c>.</summary>
    public async Task CheckHealthAsync(CancellationToken cancellationToken) =>
        await GetJsonAsync<PythonHealthResponse>(
                "health",
                health => health.Service == "buildmind-python" && health.Status == "ok",
                cancellationToken)
            .ConfigureAwait(false);

    public sealed record PythonHealthResponse(string? Service, string? Status);
}
