namespace BuildMind.Infrastructure.Persistence.Records.Requirements;

internal sealed class RequirementMutationReceiptRecord
{
    public Guid ProjectId { get; init; }
    public Guid ActorId { get; init; }
    public Guid OperationId { get; init; }
    public long AuthorizationVersion { get; init; }
    public byte Category { get; init; }
    public byte FormatVersion { get; init; }
    public required byte[] Digest { get; init; }
    public Guid SavedRevisionId { get; init; }
}
