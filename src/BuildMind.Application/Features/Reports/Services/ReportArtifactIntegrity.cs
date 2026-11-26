using System.Security.Cryptography;
using BuildMind.Domain.ValueObjects;

namespace BuildMind.Application.Features.Reports.Services;

/// <summary>Private-artifact integrity utility, not persistence or authorization evidence.</summary>
/// <remarks>Provisional isolated convention: lowercase SHA-256 prefixed with sha256:.
/// No caller may mark Ready based on this utility without independently verified storage persistence.</remarks>
public static class ReportArtifactIntegrity
{
    public static string Hash(ReadOnlySpan<byte> bytes) => "sha256:"+Convert.ToHexStringLower(SHA256.HashData(bytes));

    public static bool Matches(ReportArtifact metadata, ReadOnlySpan<byte> storedBytes, long maximumBytes)
    {
        ArgumentNullException.ThrowIfNull(metadata);
        if(maximumBytes<=0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        return metadata.MediaType=="application/pdf" && metadata.ByteLength==storedBytes.Length
            && storedBytes.Length<=maximumBytes && storedBytes.StartsWith("%PDF-"u8)
            && string.Equals(metadata.ContentHash,Hash(storedBytes),StringComparison.Ordinal);
    }
}
