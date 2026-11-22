using System.Text.Json;
using System.Text.Json.Serialization;
using BuildMind.API.Errors;
using BuildMind.API.Routing;
using BuildMind.Application.Abstractions;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Features.PlanEditor.Contracts;
using BuildMind.Application.Features.PlanEditor.Diagnostics;
using BuildMind.Application.Features.PlanEditor.Editing;
using BuildMind.Application.Features.PlanEditor.Versioning;
using Microsoft.AspNetCore.WebUtilities;

namespace BuildMind.API.Endpoints.PlanEditor;

/// <summary>
/// Isolated M3 HTTP contract for explicit candidate actions.
/// This module is deliberately absent from production DI until authentication and M4 persistence
/// adapters are approved and available.
/// The documented validation-only route is intentionally not mapped because the available application
/// orchestration persists candidates and therefore cannot satisfy its no-persistence contract.
/// </summary>
public sealed class PlanEditorEndpointModule : IApiEndpointModule
{
    private static readonly JsonSerializerOptions StrictJson = new(PlanEditorJson.StrictOptions)
    {
        AllowDuplicateProperties = false,
    };

    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        ArgumentNullException.ThrowIfNull(apiV1);

        var plans = apiV1
            .MapGroup("/projects/{projectId}/plans/{planId}")
            .WithTags("Plan Editor")
            .RequireAuthorization();

        plans.MapGet("/versions", ListRevisionsAsync)
            .WithName("ListPlanRevisions");
        plans.MapGet("/versions/{revisionId}", GetRevisionAsync)
            .WithName("GetPlanRevision");
        plans.MapPost("/candidates", CreateCandidateAsync)
            .WithName("CreatePlanEditCandidate");
        plans.MapPost("/candidates/{candidateId}/apply", ApplyCandidateAsync)
            .WithName("ApplyPlanEditCandidate");
        plans.MapPost("/candidates/{candidateId}/reject", RejectCandidateAsync)
            .WithName("RejectPlanEditCandidate");
    }

    private static async Task<IResult> ListRevisionsAsync(
        string projectId,
        string planId,
        int? pageSize,
        string? cursor,
        HttpContext context,
        ICurrentUser currentUser,
        IPlanRevisionHistoryService history,
        CancellationToken cancellationToken)
    {
        NoStore(context);
        if (!TryReadScope(projectId, planId, out var project, out var plan, out var scopeFailure))
            return scopeFailure!;
        if (!TryReadActor(currentUser, out _, out var actorFailure))
            return actorFailure!;

        var size = pageSize ?? PlanRevisionHistoryService.DefaultPageSize;
        if (size is < 1 or > PlanRevisionHistoryService.MaximumPageSize)
            return InvalidRequest(
                "pageSize",
                $"pageSize must be between 1 and {PlanRevisionHistoryService.MaximumPageSize}.");
        if (!TryDecodeCursor(cursor, out var decodedCursor))
            return InvalidRequest("cursor", "A valid revision-history cursor is required.");
        if (decodedCursor is { } scopedCursor &&
            (scopedCursor.ProjectId != project || scopedCursor.PlanId != plan))
            return InvalidRequest("cursor", "The cursor does not belong to this plan.");

        var result = await history.ListAsync(
            new PlanRevisionHistoryQuery(project, plan, size, decodedCursor),
            cancellationToken).ConfigureAwait(false);

        return MapHistoryPage(result, project, plan);
    }

    private static async Task<IResult> GetRevisionAsync(
        string projectId,
        string planId,
        string revisionId,
        HttpContext context,
        ICurrentUser currentUser,
        IPlanRevisionHistoryService history,
        CancellationToken cancellationToken)
    {
        NoStore(context);
        if (!TryReadScope(projectId, planId, out var project, out var plan, out var scopeFailure))
            return scopeFailure!;
        if (!TryReadIdentity(revisionId, "revisionId", out var revision, out var revisionFailure))
            return revisionFailure!;
        if (!TryReadActor(currentUser, out _, out var actorFailure))
            return actorFailure!;

        var result = await history.GetAsync(
            project, plan, revision, cancellationToken).ConfigureAwait(false);
        if (result is null || !Enum.IsDefined(result.Status))
            return InvalidDependency();
        if (result.Status == PlanRevisionHistoryStatus.Succeeded)
        {
            return TryMapRevision(result.Revision, project, plan, revision, out var response)
                ? Results.Ok(response)
                : InvalidDependency();
        }
        if (result.Revision is not null)
            return InvalidDependency();

        return MapHistoryFailure(result.Status);
    }

    private static async Task<IResult> CreateCandidateAsync(
        string projectId,
        string planId,
        HttpContext context,
        ICurrentUser currentUser,
        IEditValidationApplicationFacade facade,
        CancellationToken cancellationToken)
    {
        NoStore(context);
        if (!TryReadScope(projectId, planId, out var project, out var plan, out var scopeFailure))
            return scopeFailure!;
        if (!TryReadActor(currentUser, out var actor, out var actorFailure))
            return actorFailure!;
        if (!context.Request.HasJsonContentType())
            return Problem(StatusCodes.Status415UnsupportedMediaType, SharedErrorCodes.UnsupportedMediaType);

        PlanEditRequestDto? request;
        try
        {
            request = await context.Request
                .ReadFromJsonAsync<PlanEditRequestDto>(StrictJson, cancellationToken)
                .ConfigureAwait(false);
            if (request is not null)
                PlanEditorContractValidator.Validate(request);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or ArgumentException)
        {
            return InvalidRequest("request", "A complete valid candidate request is required.");
        }

        if (request is null)
            return InvalidRequest("request", "A complete candidate request is required.");
        if (request.ProjectId != project || request.PlanId != plan)
            return InvalidRequest("request", "Body project and plan identities must match the route.");

        var result = await facade.ValidateAsync(
            new CreateCandidatePreviewCommand(
                project,
                plan,
                request.BaseRevisionId,
                request.Command,
                context.TraceIdentifier),
            cancellationToken).ConfigureAwait(false);

        return MapCandidateCreation(
            result,
            actor,
            project,
            plan,
            request.BaseRevisionId,
            request.Command.CommandId);
    }

    private static async Task<IResult> ApplyCandidateAsync(
        string projectId,
        string planId,
        string candidateId,
        HttpContext context,
        ICurrentUser currentUser,
        ITrustedCandidateActionFacade facade,
        CancellationToken cancellationToken)
    {
        NoStore(context);
        if (!TryReadScope(projectId, planId, out var project, out var plan, out var scopeFailure))
            return scopeFailure!;
        if (!TryReadIdentity(candidateId, "candidateId", out var candidate, out var candidateFailure))
            return candidateFailure!;
        if (!TryReadActor(currentUser, out var actor, out var actorFailure))
            return actorFailure!;
        if (!context.Request.HasJsonContentType())
            return Problem(StatusCodes.Status415UnsupportedMediaType, SharedErrorCodes.UnsupportedMediaType);

        ApplyCandidateHttpRequest? request;
        try
        {
            request = await context.Request
                .ReadFromJsonAsync<ApplyCandidateHttpRequest>(StrictJson, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (JsonException)
        {
            return InvalidRequest("request", "A complete valid apply request is required.");
        }
        catch (NotSupportedException)
        {
            return InvalidRequest("request", "A supported JSON apply request is required.");
        }

        if (request is null)
            return InvalidRequest("request", "A complete apply request is required.");

        var result = await facade.ApplyAsync(
            new ApplyCandidateCommand(candidate, project, plan, request.AcknowledgedWarnings),
            cancellationToken).ConfigureAwait(false);

        return MapActionResult(result, actor, project, plan, candidate, CandidateActionStatus.Applied);
    }

    private static async Task<IResult> RejectCandidateAsync(
        string projectId,
        string planId,
        string candidateId,
        HttpContext context,
        ICurrentUser currentUser,
        ITrustedCandidateActionFacade facade,
        CancellationToken cancellationToken)
    {
        NoStore(context);
        if (!TryReadScope(projectId, planId, out var project, out var plan, out var scopeFailure))
            return scopeFailure!;
        if (!TryReadIdentity(candidateId, "candidateId", out var candidate, out var candidateFailure))
            return candidateFailure!;
        if (!TryReadActor(currentUser, out var actor, out var actorFailure))
            return actorFailure!;

        var result = await facade.RejectAsync(
            new CandidateActionCommand(candidate, project, plan),
            cancellationToken).ConfigureAwait(false);

        return MapActionResult(result, actor, project, plan, candidate, CandidateActionStatus.Rejected);
    }

    private static IResult MapHistoryPage(
        PlanRevisionHistoryPageResult? result,
        Guid expectedProjectId,
        Guid expectedPlanId)
    {
        if (result is null || !Enum.IsDefined(result.Status))
            return InvalidDependency();
        if (result.Status != PlanRevisionHistoryStatus.Succeeded)
        {
            if (result.Revisions is not null || result.NextCursor is not null)
                return InvalidDependency();
            return MapHistoryFailure(result.Status);
        }
        if (result.Revisions is null)
            return InvalidDependency();

        var revisions = new List<PlanRevisionSummaryHttpResponse>(result.Revisions.Count);
        var seen = new HashSet<Guid>();
        long? previousVersion = null;
        foreach (var revision in result.Revisions)
        {
            if (!TryMapRevision(
                    revision,
                    expectedProjectId,
                    expectedPlanId,
                    expectedRevisionId: null,
                    out var detail) ||
                !seen.Add(detail!.RevisionId) ||
                previousVersion is { } previous && detail.VersionNumber >= previous)
                return InvalidDependency();

            previousVersion = detail.VersionNumber;
            revisions.Add(new PlanRevisionSummaryHttpResponse(
                detail.RevisionId,
                detail.ParentRevisionId,
                detail.VersionNumber,
                detail.GeometrySchemaVersion,
                detail.GeometryHash,
                detail.LandRevisionId,
                detail.RequirementsRevisionId,
                detail.Validation.ValidationId,
                detail.CreatedBy,
                detail.CreatedAtUtc,
                detail.ChangeSummary));
        }

        string? nextCursor = null;
        if (result.NextCursor is { } cursor)
        {
            if (!cursor.IsValid || cursor.ProjectId != expectedProjectId || cursor.PlanId != expectedPlanId ||
                revisions.Count == 0)
                return InvalidDependency();
            nextCursor = EncodeCursor(cursor);
        }

        return Results.Ok(new PlanRevisionHistoryPageHttpResponse(revisions, nextCursor));
    }

    private static IResult MapHistoryFailure(PlanRevisionHistoryStatus status) => status switch
    {
        PlanRevisionHistoryStatus.DeniedOrNotFound =>
            Problem(StatusCodes.Status404NotFound, SharedErrorCodes.NotFound),
        PlanRevisionHistoryStatus.InvalidRequest =>
            InvalidRequest("request", "A valid revision-history request is required."),
        PlanRevisionHistoryStatus.InvalidCursor =>
            Problem(
                StatusCodes.Status409Conflict,
                PlanEditorErrorCodes.RevisionConflict,
                "The revision-history cursor is stale or invalid."),
        PlanRevisionHistoryStatus.DependencyUnavailable =>
            Problem(
                StatusCodes.Status503ServiceUnavailable,
                PlanEditorErrorCodes.DependencyUnavailable,
                "Revision history is temporarily unavailable."),
        PlanRevisionHistoryStatus.DependencyTimeout =>
            Problem(
                StatusCodes.Status504GatewayTimeout,
                PlanEditorErrorCodes.DependencyTimeout,
                "Revision history timed out."),
        PlanRevisionHistoryStatus.InvalidDependencyResponse or
        PlanRevisionHistoryStatus.UnexpectedInternalFailure => InvalidDependency(),
        _ => InvalidDependency(),
    };

    private static IResult MapCandidateCreation(
        CandidatePreviewResult? result,
        Guid expectedActorId,
        Guid expectedProjectId,
        Guid expectedPlanId,
        Guid expectedBaseRevisionId,
        Guid expectedCommandId)
    {
        if (result is null || !Enum.IsDefined(result.Status))
            return InvalidDependency();
        if (result.Status == CandidatePreviewResultStatus.Created)
        {
            if (result.Diagnostics is null ||
                !TryValidateCandidate(
                    result.Candidate,
                    expectedActorId,
                    expectedProjectId,
                    expectedPlanId,
                    expectedCandidateId: null,
                    expectedBaseRevisionId,
                    expectedCommandId,
                    CandidatePreviewStatus.Pending,
                    out var geometry))
                return InvalidDependency();

            var candidate = result.Candidate!;
            return Results.Json(
                new CandidateCreationHttpResponse(
                    candidate.CandidateId,
                    candidate.ProjectId,
                    candidate.PlanId,
                    candidate.BaseRevisionId,
                    candidate.GeometryHash,
                    geometry!,
                    candidate.Validation,
                    result.Diagnostics.ToArray(),
                    candidate.CreatedAtUtc,
                    candidate.ExpiresAtUtc),
                statusCode: StatusCodes.Status201Created);
        }

        if (result.Candidate is not null &&
            result.Status != CandidatePreviewResultStatus.PersistenceOutcomeUnknown)
            return InvalidDependency();

        if (result.Status is CandidatePreviewResultStatus.Unauthorized or
            CandidatePreviewResultStatus.RevisionNotFound)
            return Problem(StatusCodes.Status404NotFound, SharedErrorCodes.NotFound);

        var diagnostic = PlanEditorFailureMapper.FromCandidatePreview(result.Status);
        var status = result.Status switch
        {
            CandidatePreviewResultStatus.InvalidCommand => StatusCodes.Status400BadRequest,
            CandidatePreviewResultStatus.StaleBaseRevision or CandidatePreviewResultStatus.StaleInputs =>
                StatusCodes.Status409Conflict,
            CandidatePreviewResultStatus.CandidateRejected or CandidatePreviewResultStatus.ValidationRejected =>
                StatusCodes.Status422UnprocessableEntity,
            CandidatePreviewResultStatus.DependencyUnavailable or CandidatePreviewResultStatus.ValidationUnavailable =>
                StatusCodes.Status503ServiceUnavailable,
            CandidatePreviewResultStatus.DependencyTimeout => StatusCodes.Status504GatewayTimeout,
            _ => StatusCodes.Status500InternalServerError,
        };
        return Problem(status, diagnostic.Code, diagnostic.SafeMessage);
    }

    private static bool TryMapRevision(
        PlanRevisionSnapshot? revision,
        Guid expectedProjectId,
        Guid expectedPlanId,
        Guid? expectedRevisionId,
        out PlanRevisionDetailHttpResponse? response)
    {
        response = null;
        if (revision is null || revision.RevisionId == Guid.Empty ||
            expectedRevisionId is { } revisionId && revision.RevisionId != revisionId ||
            revision.ProjectId != expectedProjectId || revision.PlanId != expectedPlanId ||
            revision.VersionNumber <= 0 || revision.VersionNumber == long.MaxValue ||
            string.IsNullOrWhiteSpace(revision.GeometrySchemaVersion) ||
            string.IsNullOrWhiteSpace(revision.GeometryJson) ||
            string.IsNullOrWhiteSpace(revision.GeometryHash) ||
            revision.LandRevisionId == Guid.Empty || revision.RequirementsRevisionId == Guid.Empty ||
            revision.CreatedBy == Guid.Empty || revision.Validation is null ||
            revision.Validation.ValidationId == Guid.Empty ||
            !string.Equals(revision.Validation.GeometryHash, revision.GeometryHash, StringComparison.Ordinal) ||
            !string.Equals(
                PlanRevisionService.ComputeGeometryHash(revision.GeometryJson),
                revision.GeometryHash,
                StringComparison.Ordinal))
            return false;

        try
        {
            var geometry = JsonSerializer.Deserialize<PlanGeometryDto>(revision.GeometryJson, StrictJson);
            if (geometry is null)
                return false;
            PlanEditorContractValidator.Validate(geometry);
            if (geometry.ProjectId != expectedProjectId || geometry.PlanId != expectedPlanId ||
                geometry.LandRevisionId != revision.LandRevisionId ||
                geometry.RequirementsRevisionId != revision.RequirementsRevisionId ||
                !string.Equals(geometry.SchemaVersion, revision.GeometrySchemaVersion, StringComparison.Ordinal))
                return false;

            response = new PlanRevisionDetailHttpResponse(
                revision.RevisionId,
                revision.ProjectId,
                revision.PlanId,
                revision.ParentRevisionId,
                revision.VersionNumber,
                revision.GeometrySchemaVersion,
                revision.GeometryHash,
                geometry,
                revision.LandRevisionId,
                revision.RequirementsRevisionId,
                revision.Validation,
                revision.EditCommandId,
                revision.CreatedBy,
                revision.CreatedAtUtc,
                revision.ChangeSummary);
            return true;
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool TryDecodeCursor(
        string? value,
        out PlanRevisionHistoryCursor? cursor)
    {
        cursor = null;
        if (string.IsNullOrWhiteSpace(value))
            return value is null;
        if (value.Length > 1024)
            return false;

        try
        {
            var bytes = WebEncoders.Base64UrlDecode(value);
            if (bytes.Length is 0 or > 512)
                return false;
            var token = JsonSerializer.Deserialize<RevisionHistoryCursorToken>(bytes, StrictJson);
            if (token is null)
                return false;
            var decoded = new PlanRevisionHistoryCursor(
                token.ProjectId,
                token.PlanId,
                token.HeadVersionNumber,
                token.HeadRevisionId,
                token.AfterVersionNumber,
                token.AfterRevisionId,
                token.ExpectedNextRevisionId);
            if (!decoded.IsValid)
                return false;
            cursor = decoded;
            return true;
        }
        catch (Exception exception) when (exception is FormatException or JsonException or NotSupportedException)
        {
            return false;
        }
    }

    private static string EncodeCursor(PlanRevisionHistoryCursor cursor)
    {
        var token = new RevisionHistoryCursorToken(
            cursor.ProjectId,
            cursor.PlanId,
            cursor.HeadVersionNumber,
            cursor.HeadRevisionId,
            cursor.AfterVersionNumber,
            cursor.AfterRevisionId,
            cursor.ExpectedNextRevisionId);
        return WebEncoders.Base64UrlEncode(JsonSerializer.SerializeToUtf8Bytes(token, StrictJson));
    }

    private static IResult MapActionResult(
        CandidateActionResult? result,
        Guid expectedActorId,
        Guid expectedProjectId,
        Guid expectedPlanId,
        Guid expectedCandidateId,
        CandidateActionStatus expectedSuccess)
    {
        if (result is null || !Enum.IsDefined(result.Status))
            return InvalidDependency();

        if (result.Status == expectedSuccess)
        {
            var expectedCandidateStatus = expectedSuccess == CandidateActionStatus.Applied
                ? CandidatePreviewStatus.Applied
                : CandidatePreviewStatus.Rejected;
            var validCandidate = TryValidateCandidate(
                result.Candidate,
                expectedActorId,
                expectedProjectId,
                expectedPlanId,
                expectedCandidateId,
                expectedBaseRevisionId: null,
                expectedCommandId: null,
                expectedCandidateStatus,
                out _);
            var candidate = result.Candidate;

            var validRevision = expectedSuccess == CandidateActionStatus.Applied
                ? validCandidate && RevisionMatchesCandidate(result.Revision, candidate!, expectedActorId)
                : result.Revision is null;

            if (!validCandidate || !validRevision)
                return InvalidDependency();

            return Results.Ok(ToActionResponse(candidate!, result.Revision));
        }

        if (result.Status is CandidateActionStatus.Applied or CandidateActionStatus.Rejected
            || result.Revision is not null)
            return InvalidDependency();

        if (result.Status is CandidateActionStatus.Unauthorized or CandidateActionStatus.NotFound
            or CandidateActionStatus.WrongProjectOrPlan)
        {
            if (result.Candidate is not null)
                return InvalidDependency();
            return Problem(StatusCodes.Status404NotFound, SharedErrorCodes.NotFound);
        }

        if (FailureRequiresCandidate(result.Status) && result.Candidate is null)
            return InvalidDependency();

        if (result.Candidate is not null)
        {
            var expectedFailureState = result.Status switch
            {
                CandidateActionStatus.AlreadyApplied => CandidatePreviewStatus.Applied,
                CandidateActionStatus.AlreadyRejected => CandidatePreviewStatus.Rejected,
                CandidateActionStatus.Expired => CandidatePreviewStatus.Expired,
                _ => CandidatePreviewStatus.Pending,
            };
            if (!CandidateScopeMatches(
                    result.Candidate,
                    expectedActorId,
                    expectedProjectId,
                    expectedPlanId,
                    expectedCandidateId,
                    expectedFailureState))
                return InvalidDependency();
        }

        var diagnostic = PlanEditorFailureMapper.FromCandidateAction(result.Status);
        return Problem(StatusFor(result.Status), diagnostic.Code, diagnostic.SafeMessage);
    }

    private static bool FailureRequiresCandidate(CandidateActionStatus status) => status is
        CandidateActionStatus.AlreadyApplied or
        CandidateActionStatus.AlreadyRejected or
        CandidateActionStatus.Expired or
        CandidateActionStatus.StaleBaseRevision or
        CandidateActionStatus.StaleInputs or
        CandidateActionStatus.GeometryTampered or
        CandidateActionStatus.ValidationUnavailable or
        CandidateActionStatus.ValidationRejected or
        CandidateActionStatus.BlockingIssues or
        CandidateActionStatus.WarningAcknowledgementRequired or
        CandidateActionStatus.TransactionOutcomeUnknown;

    private static bool TryValidateCandidate(
        CandidatePreviewSnapshot? candidate,
        Guid expectedActorId,
        Guid expectedProjectId,
        Guid expectedPlanId,
        Guid? expectedCandidateId,
        Guid? expectedBaseRevisionId,
        Guid? expectedCommandId,
        CandidatePreviewStatus expectedStatus,
        out PlanGeometryDto? geometry)
    {
        geometry = null;
        if (candidate is null
            || !CandidateScopeMatches(
                candidate,
                expectedActorId,
                expectedProjectId,
                expectedPlanId,
                expectedCandidateId,
                expectedStatus)
            || expectedBaseRevisionId is { } baseRevisionId && candidate.BaseRevisionId != baseRevisionId
            || expectedCommandId is { } commandId && candidate.CommandId != commandId
            || candidate.BaseRevisionId == Guid.Empty
            || candidate.LandRevisionId == Guid.Empty
            || candidate.RequirementsRevisionId == Guid.Empty
            || candidate.CommandId == Guid.Empty
            || string.IsNullOrWhiteSpace(candidate.GeometryJson)
            || string.IsNullOrWhiteSpace(candidate.GeometryHash)
            || string.IsNullOrWhiteSpace(candidate.GeometrySchemaVersion)
            || candidate.Validation is null
            || candidate.Validation.ValidationId == Guid.Empty
            || string.IsNullOrWhiteSpace(candidate.Validation.ValidatorVersion)
            || string.IsNullOrWhiteSpace(candidate.Validation.RegulatoryEvidenceStatus)
            || !candidate.Validation.GeometryValid
            || candidate.Validation.HasBlockingIssues
            || !string.Equals(candidate.Validation.GeometryHash, candidate.GeometryHash, StringComparison.Ordinal)
            || !string.Equals(
                PlanRevisionService.ComputeGeometryHash(candidate.GeometryJson),
                candidate.GeometryHash,
                StringComparison.Ordinal)
            || candidate.CreatedAtUtc >= candidate.ExpiresAtUtc)
            return false;

        try
        {
            geometry = JsonSerializer.Deserialize<PlanGeometryDto>(candidate.GeometryJson, StrictJson);
            if (geometry is null)
                return false;
            PlanEditorContractValidator.Validate(geometry);
        }
        catch (Exception exception) when (exception is JsonException or ArgumentException or NotSupportedException)
        {
            geometry = null;
            return false;
        }

        return geometry.ProjectId == candidate.ProjectId
            && geometry.PlanId == candidate.PlanId
            && geometry.LandRevisionId == candidate.LandRevisionId
            && geometry.RequirementsRevisionId == candidate.RequirementsRevisionId
            && string.Equals(geometry.SchemaVersion, candidate.GeometrySchemaVersion, StringComparison.Ordinal);
    }

    private static bool CandidateScopeMatches(
        CandidatePreviewSnapshot candidate,
        Guid expectedActorId,
        Guid expectedProjectId,
        Guid expectedPlanId,
        Guid? expectedCandidateId,
        CandidatePreviewStatus expectedStatus) =>
        candidate.OwnerId == expectedActorId
        && candidate.ProjectId == expectedProjectId
        && candidate.PlanId == expectedPlanId
        && (expectedCandidateId is null || candidate.CandidateId == expectedCandidateId)
        && candidate.CandidateId != Guid.Empty
        && candidate.Status == expectedStatus;

    private static bool RevisionMatchesCandidate(
        PlanRevisionSnapshot? revision,
        CandidatePreviewSnapshot candidate,
        Guid expectedActorId) =>
        revision is not null
        && revision.RevisionId != Guid.Empty
        && revision.ProjectId == candidate.ProjectId
        && revision.PlanId == candidate.PlanId
        && revision.ParentRevisionId == candidate.BaseRevisionId
        && revision.VersionNumber > 0
        && string.Equals(revision.GeometrySchemaVersion, candidate.GeometrySchemaVersion, StringComparison.Ordinal)
        && string.Equals(revision.GeometryJson, candidate.GeometryJson, StringComparison.Ordinal)
        && string.Equals(revision.GeometryHash, candidate.GeometryHash, StringComparison.Ordinal)
        && revision.LandRevisionId == candidate.LandRevisionId
        && revision.RequirementsRevisionId == candidate.RequirementsRevisionId
        && ValidationMatches(revision.Validation, candidate.Validation)
        && revision.EditCommandId == candidate.CommandId
        && revision.CreatedBy == expectedActorId;

    private static bool ValidationMatches(
        PlanValidationReferenceDto actual,
        PlanValidationReferenceDto expected) =>
        actual.ValidationId == expected.ValidationId
        && string.Equals(actual.ValidatorVersion, expected.ValidatorVersion, StringComparison.Ordinal)
        && string.Equals(actual.GeometryHash, expected.GeometryHash, StringComparison.Ordinal)
        && actual.GeometryValid == expected.GeometryValid
        && actual.RequirementsMatch == expected.RequirementsMatch
        && string.Equals(actual.RegulatoryEvidenceStatus, expected.RegulatoryEvidenceStatus, StringComparison.Ordinal)
        && actual.HasBlockingIssues == expected.HasBlockingIssues
        && actual.ValidatedAtUtc.EqualsExact(expected.ValidatedAtUtc);

    private static CandidateActionHttpResponse ToActionResponse(
        CandidatePreviewSnapshot candidate,
        PlanRevisionSnapshot? revision) => new(
        candidate.CandidateId,
        candidate.ProjectId,
        candidate.PlanId,
        candidate.Status.ToString(),
        revision is null
            ? null
            : new AppliedRevisionHttpResponse(
                revision.RevisionId,
                revision.ParentRevisionId!.Value,
                revision.VersionNumber));

    private static int StatusFor(CandidateActionStatus status) => status switch
    {
        CandidateActionStatus.Unauthorized or CandidateActionStatus.NotFound or
            CandidateActionStatus.WrongProjectOrPlan => StatusCodes.Status404NotFound,
        CandidateActionStatus.AlreadyApplied or CandidateActionStatus.AlreadyRejected or
            CandidateActionStatus.Expired or CandidateActionStatus.StaleBaseRevision or
            CandidateActionStatus.StaleInputs => StatusCodes.Status409Conflict,
        CandidateActionStatus.ValidationRejected or CandidateActionStatus.BlockingIssues or
            CandidateActionStatus.WarningAcknowledgementRequired => StatusCodes.Status422UnprocessableEntity,
        CandidateActionStatus.DependencyUnavailable or CandidateActionStatus.ValidationUnavailable =>
            StatusCodes.Status503ServiceUnavailable,
        CandidateActionStatus.DependencyTimeout => StatusCodes.Status504GatewayTimeout,
        _ => StatusCodes.Status500InternalServerError,
    };

    private static bool TryReadScope(
        string projectId,
        string planId,
        out Guid project,
        out Guid plan,
        out IResult? failure)
    {
        if (!TryReadIdentity(projectId, "projectId", out project, out failure))
        {
            plan = Guid.Empty;
            return false;
        }

        return TryReadIdentity(planId, "planId", out plan, out failure);
    }

    private static bool TryReadIdentity(
        string value,
        string field,
        out Guid identity,
        out IResult? failure)
    {
        if (Guid.TryParse(value, out identity) && identity != Guid.Empty)
        {
            failure = null;
            return true;
        }

        failure = InvalidRequest(field, $"A nonempty {field} is required.");
        return false;
    }

    private static bool TryReadActor(
        ICurrentUser currentUser,
        out Guid actorId,
        out IResult? failure)
    {
        ArgumentNullException.ThrowIfNull(currentUser);
        if (currentUser.UserId is { } resolved && resolved != Guid.Empty)
        {
            actorId = resolved;
            failure = null;
            return true;
        }

        actorId = Guid.Empty;
        failure = Problem(StatusCodes.Status401Unauthorized, SharedErrorCodes.Unauthenticated);
        return false;
    }

    private static IResult InvalidRequest(string field, string message) =>
        ApiProblems.ValidationFailed(new Dictionary<string, string[]> { [field] = [message] });

    private static IResult InvalidDependency() => Problem(
        StatusCodes.Status500InternalServerError,
        PlanEditorErrorCodes.InvalidDependencyResponse,
        ProblemDetailsDefaults.InternalErrorDetail);

    private static IResult Problem(int status, string code, string? detail = null) =>
        Results.Problem(
            statusCode: status,
            type: ProblemDetailsDefaults.GetTypeForCode(code),
            detail: status == StatusCodes.Status500InternalServerError
                ? ProblemDetailsDefaults.InternalErrorDetail
                : detail,
            extensions: new Dictionary<string, object?>
            {
                [ProblemDetailsDefaults.CodeExtension] = code,
            });

    private static void NoStore(HttpContext context) =>
        context.Response.Headers.CacheControl = "no-store";

    private sealed record RevisionHistoryCursorToken(
        Guid ProjectId,
        Guid PlanId,
        long HeadVersionNumber,
        Guid HeadRevisionId,
        long AfterVersionNumber,
        Guid AfterRevisionId,
        Guid ExpectedNextRevisionId);
}

public sealed record PlanRevisionHistoryPageHttpResponse(
    IReadOnlyList<PlanRevisionSummaryHttpResponse> Revisions,
    string? NextCursor);

public sealed record PlanRevisionSummaryHttpResponse(
    Guid RevisionId,
    Guid? ParentRevisionId,
    long VersionNumber,
    string GeometrySchemaVersion,
    string GeometryHash,
    Guid LandRevisionId,
    Guid RequirementsRevisionId,
    Guid ValidationId,
    Guid CreatedBy,
    DateTimeOffset CreatedAtUtc,
    string? ChangeSummary);

public sealed record PlanRevisionDetailHttpResponse(
    Guid RevisionId,
    Guid ProjectId,
    Guid PlanId,
    Guid? ParentRevisionId,
    long VersionNumber,
    string GeometrySchemaVersion,
    string GeometryHash,
    PlanGeometryDto Geometry,
    Guid LandRevisionId,
    Guid RequirementsRevisionId,
    PlanValidationReferenceDto Validation,
    Guid? EditCommandId,
    Guid CreatedBy,
    DateTimeOffset CreatedAtUtc,
    string? ChangeSummary);

public sealed record CandidateCreationHttpResponse(
    Guid CandidateId,
    Guid ProjectId,
    Guid PlanId,
    Guid BaseRevisionId,
    string GeometryHash,
    PlanGeometryDto CandidateGeometry,
    PlanValidationReferenceDto Validation,
    IReadOnlyList<PlanEditDiagnosticDto> Diagnostics,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset ExpiresAtUtc);

public sealed record ApplyCandidateHttpRequest(
    [property: JsonPropertyName("acknowledgedWarnings")] bool AcknowledgedWarnings);

/// <summary>Provisional reduced response for an explicit candidate action.</summary>
public sealed record CandidateActionHttpResponse(
    Guid CandidateId,
    Guid ProjectId,
    Guid PlanId,
    string Status,
    AppliedRevisionHttpResponse? Revision);

public sealed record AppliedRevisionHttpResponse(
    Guid RevisionId,
    Guid ParentRevisionId,
    long VersionNumber);
