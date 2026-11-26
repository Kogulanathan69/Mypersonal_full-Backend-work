using BuildMind.API.Routing;
using BuildMind.Application.Abstractions.Errors;
using BuildMind.Application.Features.Survey.Contracts;
using BuildMind.Application.Features.Survey.Services;

namespace BuildMind.API.Endpoints.Survey;

public sealed class SurveyEndpointModule : IApiEndpointModule
{
    public void MapEndpoints(RouteGroupBuilder apiV1)
    {
        ArgumentNullException.ThrowIfNull(apiV1);

        var group = apiV1
            .MapGroup("/projects/{id}/surveys")
            .WithTags("Survey")
            .RequireAuthorization();

        group.MapPost("", UploadAsync)
            .WithName("UploadProjectSurvey")
            .WithMetadata(new Microsoft.AspNetCore.Mvc.RequestSizeLimitAttribute(7L * 1024 * 1024))
            .Produces(202);

        group.MapGet("/{surveyId}", GetStatusAsync)
            .WithName("GetProjectSurveyStatus");
    }

    private static async Task<IResult> UploadAsync(
        string id,
        HttpContext context,
        SurveyUploadService service,
        BuildMind.Application.Abstractions.ICurrentUser currentUser,
        BuildMind.Application.Features.PlanEditor.Confirmation.IProjectOwnershipGate ownershipGate,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";

        if (!Guid.TryParse(id, out Guid projectId) || projectId == Guid.Empty)
            return Results.BadRequest(new { code = "INVALID_PROJECT_ID" });

        if (!context.Request.HasFormContentType)
            return Results.Problem(statusCode: 415);

        try
        {
            if (!currentUser.IsAuthenticated)
                return Results.Problem(statusCode: 401);

            Guid userId = currentUser.GetRequiredUserId();

            if (userId == Guid.Empty || currentUser.UserId != userId)
                return Results.Problem(statusCode: 401);

            bool ownsProject = await ownershipGate.OwnsProjectAsync(
                projectId, userId, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            if (!ownsProject)
                return Results.Problem(statusCode: 404);

            var formOptions = new Microsoft.AspNetCore.Http.Features.FormOptions
            {
                MultipartBodyLengthLimit = 7L * 1024 * 1024,
                MultipartBoundaryLengthLimit = 128,
                MultipartHeadersCountLimit = 16,
                MultipartHeadersLengthLimit = 16 * 1024,
                ValueCountLimit = 16,
                ValueLengthLimit = 16 * 1024,
                MemoryBufferThreshold = 64 * 1024
            };

            IFormCollection form;
            try
            {
                form = await context.Request.ReadFormAsync(
                    formOptions, cancellationToken);
            }
            catch (IOException exception) when (
                exception.GetType() == typeof(IOException) &&
                !cancellationToken.IsCancellationRequested &&
                !context.RequestAborted.IsCancellationRequested &&
                exception.Message.StartsWith(
                    "Unexpected end of Stream",
                    StringComparison.Ordinal))
            {
                return Results.Problem(
                    statusCode: 400,
                    extensions: new Dictionary<string, object?>
                    {
                        ["code"] = "INVALID_REQUEST"
                    });
            }
            if (form.Files.Count > 1)
                return Results.BadRequest(new { code = "MULTIPLE_FILES_NOT_ALLOWED" });

            var file = form.Files.GetFile("file");

            if (file is null)
                return Results.BadRequest(new { code = "FILE_REQUIRED" });

            await using var stream = file.OpenReadStream();

            var request = new UploadSurveyRequest(
                projectId,
                file.FileName,
                file.ContentType,
                file.Length,
                stream);

            var result = await service.UploadAsync(request, cancellationToken);

            return Results.Accepted(value: result);
        }
        catch (UnauthenticatedAccessException)
        {
            return Results.Problem(statusCode: 401);
        }
        catch (KeyNotFoundException)
        {
            return Results.Problem(statusCode: 404);
        }
        catch (InvalidDataException exception)
        {
            string code = exception.Message;

            int status = code switch
            {
                "INVALID_FILE_SIZE" => 413,
                "UNSUPPORTED_MEDIA_TYPE" => 415,
                _ => 400
            };

            return Results.Problem(
                statusCode: status,
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = code switch
                    {
                        "INVALID_FILE_SIZE" => "INVALID_FILE_SIZE",
                        "UNSUPPORTED_MEDIA_TYPE" => "UNSUPPORTED_MEDIA_TYPE",
                        "INVALID_FILE_NAME" => "INVALID_FILE_NAME",
                        "INVALID_FILE_CONTENT" => "INVALID_FILE_CONTENT",
                        "FILE_SIZE_MISMATCH" => "FILE_SIZE_MISMATCH",
                        "FILE_SIGNATURE_MISMATCH" => "FILE_SIGNATURE_MISMATCH",
                        _ => "INVALID_REQUEST"
                    }
                });
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { code = "INVALID_REQUEST" });
        }

    }

    private static async Task<IResult> GetStatusAsync(
        string id,
        string surveyId,
        HttpContext context,
        SurveyStatusService service,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";

        if (!Guid.TryParse(id, out Guid projectId) || projectId == Guid.Empty)
            return Results.BadRequest(new { code = "INVALID_PROJECT_ID" });

        if (!Guid.TryParse(surveyId, out Guid parsedSurveyId) || parsedSurveyId == Guid.Empty)
            return Results.BadRequest(new { code = "INVALID_SURVEY_ID" });

        try
        {
            SurveyStatusResponse? result = await service.GetAsync(
                projectId,
                parsedSurveyId,
                cancellationToken);

            return result is null
                ? Results.NotFound()
                : Results.Ok(result);
        }
        catch (UnauthenticatedAccessException)
        {
            return Results.Problem(statusCode: 401);
        }
        catch (ArgumentException)
        {
            return Results.BadRequest(new { code = "INVALID_REQUEST" });
        }
    }
}
