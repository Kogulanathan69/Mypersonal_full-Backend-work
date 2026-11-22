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
            .WithName("UploadProjectSurvey");

        group.MapGet("/{surveyId}", GetStatusAsync)
            .WithName("GetProjectSurveyStatus");
    }

    private static async Task<IResult> UploadAsync(
        string id,
        HttpContext context,
        SurveyUploadService service,
        CancellationToken cancellationToken)
    {
        context.Response.Headers.CacheControl = "no-store";

        if (!Guid.TryParse(id, out Guid projectId) || projectId == Guid.Empty)
            return Results.BadRequest(new { code = "INVALID_PROJECT_ID" });

        if (!context.Request.HasFormContentType)
            return Results.Problem(statusCode: 415);

        try
        {
            var form = await context.Request.ReadFormAsync(cancellationToken);
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
                    ["code"] = code
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
