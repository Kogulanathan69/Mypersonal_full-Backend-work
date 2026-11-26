using System.Security.Cryptography;

namespace BuildMind.Application.Features.PlanEditor.Integration;

/// <summary>
/// SHA-256 identity of an already-canonical command representation.
/// The cross-member decision defining the canonical business fields remains outstanding; this type
/// intentionally hashes bytes supplied by that future policy instead of selecting fields itself.
/// </summary>
public readonly record struct CommandFingerprint
{
    private const string Prefix = "sha256:";

    private CommandFingerprint(string value) => Value = value;

    public string? Value { get; }

    public bool IsValid =>
        Value is { Length: 71 } &&
        Value.StartsWith(Prefix, StringComparison.Ordinal) &&
        Value.AsSpan(Prefix.Length).IndexOfAnyExcept("0123456789abcdef") < 0;

    public static CommandFingerprint Compute(ReadOnlySpan<byte> canonicalCommand)
    {
        if (canonicalCommand.IsEmpty)
            throw new ArgumentException("A canonical command representation is required.", nameof(canonicalCommand));

        var hash = SHA256.HashData(canonicalCommand);
        return new CommandFingerprint(Prefix + Convert.ToHexString(hash).ToLowerInvariant());
    }

    public bool Matches(CommandFingerprint other) =>
        IsValid && other.IsValid && string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override string ToString() => Value ?? string.Empty;
}
