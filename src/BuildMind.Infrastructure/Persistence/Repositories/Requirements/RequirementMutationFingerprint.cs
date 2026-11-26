using System.Buffers.Binary;
using System.Security.Cryptography;

namespace BuildMind.Infrastructure.Persistence.Repositories.Requirements;

// Isolated draft envelope. M5 must still define the canonical payload and operation categories.
// Callers must provide server-canonical bytes, never bytes re-read from a MySQL JSON column.
internal static class RequirementMutationFingerprint
{
    public static byte[] Compute(Guid projectId, Guid actorId, Guid operationId, long authorizationVersion,
        Guid? expectedRevisionId, ReadOnlySpan<byte> canonicalPayload)
    {
        if (projectId == Guid.Empty || actorId == Guid.Empty || operationId == Guid.Empty ||
            expectedRevisionId == Guid.Empty || authorizationVersion <= 0 || canonicalPayload.IsEmpty)
        {
            throw new ArgumentException("Invalid requirements mutation scope or canonical payload.");
        }

        Span<byte> envelope = stackalloc byte[77];
        projectId.TryWriteBytes(envelope[..16], bigEndian: true, out _);
        actorId.TryWriteBytes(envelope[16..32], bigEndian: true, out _);
        operationId.TryWriteBytes(envelope[32..48], bigEndian: true, out _);
        BinaryPrimitives.WriteInt64BigEndian(envelope[48..56], authorizationVersion);
        envelope[56] = expectedRevisionId.HasValue ? (byte)1 : (byte)0;
        expectedRevisionId.GetValueOrDefault().TryWriteBytes(envelope[57..73], bigEndian: true, out _);
        BinaryPrimitives.WriteInt32BigEndian(envelope[73..], canonicalPayload.Length);

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("BuildMind.Requirements.Replacement.V1\0"u8);
        hash.AppendData(envelope);
        hash.AppendData(canonicalPayload);
        return hash.GetHashAndReset();
    }
}
