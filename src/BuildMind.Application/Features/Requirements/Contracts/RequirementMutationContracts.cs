namespace BuildMind.Application.Features.Requirements.Contracts;

public enum RequirementMutationCategory : byte { Full = 1, Spaces, Relationships, DesignMethod }

public sealed record RequirementMutation(RequirementMutationCategory Category, SaveRequirementsRequest Fields);

public sealed record RequirementMutationScope(Guid ProjectId, Guid ActorId, Guid OperationId, long Version);

public enum RequirementMutationStatus { Absent, Committed, Denied, Reused, Unknown, Failed }

public sealed record RequirementMutationResult(RequirementMutationStatus Status, RequirementRevisionDto? Revision = null,
    RequirementsOperationResult? Failure = null);
