using System.Net;
using System.Text.Json;

namespace BuildMind.Infrastructure.Tests.Support;

/// <summary>Parsed RFC 9457 problem response used by assertions.</summary>
public sealed record ProblemResponse(HttpStatusCode Status, string? MediaType, string RawBody, JsonElement Root)
{
    public static async Task<ProblemResponse> ReadAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        return new ProblemResponse(
            response.StatusCode,
            response.Content.Headers.ContentType?.MediaType,
            body,
            document.RootElement.Clone());
    }

    public string? GetString(string name) =>
        Root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    public int? GetInt(string name) =>
        Root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;

    public bool Has(string name) => Root.TryGetProperty(name, out _);
}
