using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Infrastructure.Configuration;

namespace BuildMind.Infrastructure.Integration;

/// <summary>
/// Hardened JSON client for one private internal service (BE-M4-D05). Feature-specific clients wrap it; it is not
/// a public API and never receives a destination from a user.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><b>Destination:</b> only the validated configured <c>BaseUrl</c> (C8). Callers pass a code-defined
/// relative path, which must resolve under the base URL (absolute URLs, <c>//host</c>, backslashes and <c>..</c>
/// escapes are rejected), so there is no SSRF through this client.</item>
/// <item><b>Transport</b> (configured in the API composition root): no automatic redirects (any 3xx is an
/// unexpected response), no cookies, no automatic decompression, IHttpClientFactory pooling, framework request
/// logging removed (it would log internal URLs).</item>
/// <item><b>Headers:</b> only <c>Accept</c>, <c>Content-Type</c> and W3C <c>traceparent</c>. User credentials,
/// cookies and incoming headers are never forwarded. Internal service authentication is NOT implemented (G11).</item>
/// <item><b>Bounds:</b> the configured <c>TimeoutSeconds</c>; responses larger than <see cref="MaxResponseBytes"/>
/// are rejected; caller cancellation propagates as <see cref="OperationCanceledException"/>.</item>
/// <item><b>Failures</b> become <see cref="DependencyFailureException"/> with no URL, host or payload in it.
/// <b>No retries</b>: generation and editing calls are not idempotent; safe retries need an approved idempotency
/// contract first.</item>
/// </list>
/// </remarks>
public class InternalServiceClient
{
    public const int DefaultMaxResponseBytes = 4 * 1024 * 1024;

    /// <summary>Web defaults (camelCase, case-insensitive) with an explicit nesting limit for untrusted responses.</summary>
    /// <summary>
    /// M6-review correction: strict contracts. Duplicate properties and unknown members are rejected; the raw document
    /// is validated first (no comments, no trailing commas, depth, object root) BEFORE typed deserialization.
    /// </summary>
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        // Web defaults are lenient; internal contracts are exact (M6 H4-R1 C11): member names match case-sensitively
        // and numbers are accepted only as JSON numbers, never as strings.
        PropertyNameCaseInsensitive = false,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
        MaxDepth = 64,
        AllowDuplicateProperties = false,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
    };

    private static readonly JsonDocumentOptions StrictDocument = new()
    {
        MaxDepth = 64,
        AllowDuplicateProperties = false,
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
    };

    private readonly HttpClient http;
    private readonly InternalServiceOptions options;
    private readonly InternalServiceBulkhead bulkhead;
    private readonly Uri? baseUri;

    /// <param name="bulkhead">Shared per-host limiter (DI singleton). When omitted, the client gets its own.</param>
    public InternalServiceClient(
        HttpClient http,
        InternalServiceOptions options,
        string dependency,
        int maxResponseBytes = DefaultMaxResponseBytes,
        InternalServiceBulkhead? bulkhead = null)
    {
        this.http = http ?? throw new ArgumentNullException(nameof(http));
        this.options = options ?? throw new ArgumentNullException(nameof(options));
        ArgumentException.ThrowIfNullOrWhiteSpace(dependency);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxResponseBytes);
        Dependency = dependency;
        MaxResponseBytes = maxResponseBytes;
        this.bulkhead = bulkhead ?? new InternalServiceBulkhead();

        // Defence in depth: start-up validation (C8) already enforces this, but the client never trusts a base URL
        // that is not plain http/https without credentials, query or fragment; such a client stays disabled.
        if (options.Enabled
            && Uri.TryCreate(options.BaseUrl?.Trim(), UriKind.Absolute, out var configured)
            && (configured.Scheme == Uri.UriSchemeHttp || configured.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(configured.Host)
            && string.IsNullOrEmpty(configured.UserInfo)
            && string.IsNullOrEmpty(configured.Query)
            && string.IsNullOrEmpty(configured.Fragment))
        {
            // Trailing slash so code-defined relative paths extend the base path instead of replacing it.
            baseUri = configured.AbsoluteUri.EndsWith('/') ? configured : new Uri(configured.AbsoluteUri + "/");
        }
    }

    /// <summary>Fixed code-defined dependency name used in errors and logs (for example "python").</summary>
    public string Dependency { get; }

    public int MaxResponseBytes { get; }

    public bool IsEnabled => baseUri is not null;

    public Task<T> GetJsonAsync<T>(string relativePath, Func<T, bool>? isValid, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Get, relativePath, content: null, isValid, isValidRaw: null, cancellationToken);

    /// <summary>Single POST, never retried.</summary>
    public Task<TResponse> PostJsonAsync<TRequest, TResponse>(
        string relativePath, TRequest body, Func<TResponse, bool>? isValid, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, relativePath, JsonContent(body), isValid, isValidRaw: null, cancellationToken);

    /// <summary>
    /// Single POST, never retried, whose validator also receives the strictly parsed raw root element, so integrity
    /// checks (for example an independently recomputed digest) run over exactly what was received.
    /// </summary>
    public Task<TResponse> PostJsonVerifiedAsync<TRequest, TResponse>(
        string relativePath, TRequest body, Func<TResponse, JsonElement, bool> isValidRaw, CancellationToken cancellationToken) =>
        SendAsync(HttpMethod.Post, relativePath, JsonContent(body), isValid: null, isValidRaw, cancellationToken);

    /// <summary>
    /// Resolves a code-defined relative path under the base URL, or throws. ALLOWLIST (STEP 08-B): one or more
    /// segments of <c>[A-Za-z0-9_-]</c> separated by single <c>/</c>. This rejects every encoding trick at once:
    /// percent-encoding (including <c>%2e%2e</c>, <c>%2f</c>, double encoding), dot segments, empty segments,
    /// backslashes, schemes, authorities, query strings and fragments. The resolved URI must also keep the base's
    /// scheme, host, port and path prefix.
    /// </summary>
    public Uri Resolve(string relativePath)
    {
        if (baseUri is null)
        {
            throw new DependencyFailureException(Dependency, DependencyFailureKind.Disabled);
        }

        if (relativePath is null || !IsAllowedPath(relativePath))
        {
            throw new ArgumentException("Internal service paths must be code-defined relative paths under the base URL.", nameof(relativePath));
        }

        var target = new Uri(baseUri, new Uri(relativePath, UriKind.Relative));
        if (target.Scheme != baseUri.Scheme
            || !string.Equals(target.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase)
            || target.Port != baseUri.Port
            || !target.AbsolutePath.StartsWith(baseUri.AbsolutePath, StringComparison.Ordinal)
            || !string.IsNullOrEmpty(target.Query)
            || !string.IsNullOrEmpty(target.Fragment))
        {
            throw new ArgumentException("Internal service paths must stay under the configured base URL.", nameof(relativePath));
        }

        return target;
    }

    private static bool IsAllowedPath(string path)
    {
        if (path.Length is 0 or > 256)
        {
            return false;
        }

        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0 || segment.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '_' or '-')))
            {
                return false; // empty segment ("//", leading/trailing "/") or any other character ('.', '%', '?', '#', '\', ':', ' ', non-ASCII)
            }
        }

        return true;
    }

    private async Task<T> SendAsync<T>(
        HttpMethod method, string relativePath, HttpContent? content, Func<T, bool>? isValid, Func<T, JsonElement, bool>? isValidRaw,
        CancellationToken cancellationToken)
    {
        var target = Resolve(relativePath);

        // STEP 08-C: bounded in-flight + queued calls per dependency; saturation fails fast without network I/O.
        using var slot = bulkhead.TryEnter(Dependency) ?? throw Failure(DependencyFailureKind.Unavailable);

        using var request = new HttpRequestMessage(method, target) { Content = content };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(options.AuthToken))
        {
            // G11 MVP: only ever sent to the validated base URL (Resolve), never logged (framework loggers removed).
            request.Headers.TryAddWithoutValidation(InternalServiceOptions.AuthHeaderName, options.AuthToken);
        }

        PropagateTraceContext(request);

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // the caller cancelled: not a dependency failure
        }
        catch (OperationCanceledException)
        {
            throw Failure(DependencyFailureKind.Timeout);
        }
        catch (HttpRequestException)
        {
            throw Failure(DependencyFailureKind.Unavailable);
        }

        using (response)
        {
            var status = (int)response.StatusCode;
            if (!response.IsSuccessStatusCode)
            {
                throw Failure(
                    response.StatusCode switch
                    {
                        HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable => DependencyFailureKind.Unavailable,
                        HttpStatusCode.GatewayTimeout => DependencyFailureKind.Timeout,
                        _ => DependencyFailureKind.UnexpectedResponse, // includes every redirect (never followed)
                    },
                    status);
            }

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            if (mediaType is null
                || !(mediaType.Equals("application/json", StringComparison.OrdinalIgnoreCase)
                     || mediaType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)))
            {
                throw Failure(DependencyFailureKind.MalformedResponse, status);
            }

            if (response.Content.Headers.ContentLength > MaxResponseBytes)
            {
                throw Failure(DependencyFailureKind.ResponseTooLarge, status);
            }

            byte[] body;
            try
            {
                body = await ReadBoundedAsync(response.Content, deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (OperationCanceledException)
            {
                throw Failure(DependencyFailureKind.Timeout, status);
            }
            catch (IOException)
            {
                throw Failure(DependencyFailureKind.Unavailable, status);
            }

            using var document = ParseStrictObject(body) ?? throw Failure(DependencyFailureKind.MalformedResponse, status);

            T? value;
            try
            {
                value = document.RootElement.Deserialize<T>(Json);
            }
            catch (JsonException)
            {
                throw Failure(DependencyFailureKind.MalformedResponse, status);
            }

            if (value is null || (isValid is not null && !isValid(value))
                || (isValidRaw is not null && !isValidRaw(value, document.RootElement)))
            {
                throw Failure(DependencyFailureKind.MalformedResponse, status);
            }

            return value;
        }
    }

    private async Task<byte[]> ReadBoundedAsync(HttpContent content, CancellationToken cancellationToken)
    {
        await using var stream = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[16 * 1024];
        int read;
        while ((read = await stream.ReadAsync(chunk, cancellationToken).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxResponseBytes)
            {
                throw Failure(DependencyFailureKind.ResponseTooLarge, null);
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    private static HttpContent JsonContent<TRequest>(TRequest body)
    {
        var content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(body, Json));
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return content;
    }

    /// <summary>
    /// Explicit W3C propagation from the current activity (the request's trace). SocketsHttpHandler's diagnostics
    /// handler also propagates, and skips the header when it is already present, so it is never duplicated.
    /// </summary>
    /// <summary>Raw validation before deserialization: well-formed, no duplicate properties, bounded depth, object root.</summary>
    internal static bool IsStrictJsonObject(ReadOnlySpan<byte> body)
    {
        using var document = ParseStrictObject(body.ToArray());
        return document is not null;
    }

    private static JsonDocument? ParseStrictObject(byte[] body)
    {
        JsonDocument? document = null;
        try
        {
            document = JsonDocument.Parse(body, StrictDocument);
            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                var result = document;
                document = null;
                return result;
            }

            return null;
        }
        catch (JsonException)
        {
            return null;
        }
        finally
        {
            document?.Dispose();
        }
    }

    private static void PropagateTraceContext(HttpRequestMessage request)
    {
        var activity = Activity.Current;
        if (activity is { IdFormat: ActivityIdFormat.W3C } && !request.Headers.Contains("traceparent"))
        {
            request.Headers.TryAddWithoutValidation("traceparent", activity.Id);
            if (!string.IsNullOrEmpty(activity.TraceStateString))
            {
                request.Headers.TryAddWithoutValidation("tracestate", activity.TraceStateString);
            }
        }
    }

    private DependencyFailureException Failure(DependencyFailureKind kind, int? status = null) => new(Dependency, kind, status);
}
