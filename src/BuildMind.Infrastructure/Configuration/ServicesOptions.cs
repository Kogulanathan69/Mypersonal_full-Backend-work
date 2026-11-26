namespace BuildMind.Infrastructure.Configuration;

/// <summary>
/// Configured internal service destinations (configuration section <c>Services</c>; decision C8).
/// </summary>
public sealed class ServicesOptions
{
    public const string SectionName = "Services";

    public InternalServiceOptions Python { get; set; } = new();

    public InternalServiceOptions Rag { get; set; } = new();

    /// <summary>
    /// Returns validation failures. Messages name configuration keys only and never echo configured
    /// values, which could contain credentials.
    /// </summary>
    /// <param name="requireRagAuthToken">
    /// G11 (MVP): true outside Development, so an enabled RAG service must have an internal auth token (fail closed).
    /// </param>
    /// <param name="enforceSecureTransport">True outside Development: a token must not travel over plaintext http to a
    /// non-loopback host unless <see cref="InternalServiceOptions.AllowPlaintextPrivateNetwork"/> is set.</param>
    /// <param name="production">True in Production: every enabled service must use https or loopback (token or not)
    /// and the plaintext exception is refused.</param>
    /// <param name="isolatedTestEnvironment">True only in Development and Testing, the only environments where
    /// <see cref="InternalServiceOptions.AllowSyntheticConstraints"/> may be set.</param>
    public IReadOnlyList<string> Validate(
        bool requireRagAuthToken = false, bool enforceSecureTransport = false, bool production = false, bool isolatedTestEnvironment = true)
    {
        var failures = new List<string>();
        ValidateService(Python, $"{SectionName}:Python", failures);
        ValidateService(Rag, $"{SectionName}:Rag", failures);
        if (enforceSecureTransport || production)
        {
            ValidateTransport(Python, $"{SectionName}:Python", production, failures);
            ValidateTransport(Rag, $"{SectionName}:Rag", production, failures);
        }

        if ((Python?.AllowSyntheticConstraints ?? false) || ((Rag?.AllowSyntheticConstraints ?? false) && !isolatedTestEnvironment))
        {
            failures.Add($"{SectionName}:Rag:AllowSyntheticConstraints is allowed only in the Development and Testing environments (and only for Rag).");
        }

        if (requireRagAuthToken && Rag is { Enabled: true } && string.IsNullOrEmpty(Rag.AuthToken))
        {
            failures.Add($"{SectionName}:Rag:AuthToken is required outside Development when {SectionName}:Rag:Enabled is true.");
        }

        return failures;
    }

    private static void ValidateService(InternalServiceOptions? service, string key, List<string> failures)
    {
        if (service is null)
        {
            failures.Add($"{key} must not be null.");
            return;
        }

        var hasUrl = !string.IsNullOrWhiteSpace(service.BaseUrl);

        if (service.Enabled && !hasUrl)
        {
            failures.Add($"{key}:BaseUrl is required when {key}:Enabled is true.");
        }

        if (hasUrl && !IsAcceptableBaseUrl(service.BaseUrl!))
        {
            failures.Add(
                $"{key}:BaseUrl must be an absolute http or https URL without credentials, query or fragment.");
        }

        if (service.AuthToken is not null && !IsAcceptableAuthToken(service.AuthToken))
        {
            failures.Add(
                $"{key}:AuthToken must be {InternalServiceOptions.MinAuthTokenLength} to {InternalServiceOptions.MaxAuthTokenLength} printable ASCII characters without spaces.");
        }

        if (service.TimeoutSeconds is < InternalServiceOptions.MinTimeoutSeconds or > InternalServiceOptions.MaxTimeoutSeconds)
        {
            failures.Add(
                $"{key}:TimeoutSeconds must be between {InternalServiceOptions.MinTimeoutSeconds} and {InternalServiceOptions.MaxTimeoutSeconds}.");
        }
    }

    private static void ValidateTransport(InternalServiceOptions? service, string key, bool production, List<string> failures)
    {
        if (service is null)
        {
            return;
        }

        if (production && service.AllowPlaintextPrivateNetwork)
        {
            failures.Add($"{key}:AllowPlaintextPrivateNetwork is not allowed in Production.");
        }

        if (!service.Enabled || !Uri.TryCreate(service.BaseUrl?.Trim(), UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttp || uri.IsLoopback)
        {
            return;
        }

        if (production)
        {
            failures.Add($"{key}:BaseUrl must use https (or a loopback address) in Production.");
        }
        else if (!string.IsNullOrEmpty(service.AuthToken) && !service.AllowPlaintextPrivateNetwork)
        {
            failures.Add($"{key}:BaseUrl must use https (or a loopback address) when {key}:AuthToken is set, unless {key}:AllowPlaintextPrivateNetwork is explicitly true.");
        }
    }

    /// <summary>Header-safe: printable ASCII without whitespace (no CR/LF injection), bounded length.</summary>
    internal static bool IsAcceptableAuthToken(string token) =>
        token.Length is >= InternalServiceOptions.MinAuthTokenLength and <= InternalServiceOptions.MaxAuthTokenLength
        && token.All(c => c is > ' ' and < (char)127);

    private static bool IsAcceptableBaseUrl(string value)
    {
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        return (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && !string.IsNullOrEmpty(uri.Host)
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment);
    }
}
