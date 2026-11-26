using BuildMind.Application.Common.Validation;
using BuildMind.Application.Features.Projects.DTOs;

namespace BuildMind.Application.Features.Projects.Validators;

public sealed class CreateProjectRequestValidator
    : IValidator<CreateProjectRequest>
{
    public ValidationResult Validate(CreateProjectRequest request)
    {
        var result = new ValidationResult();

        if (request is null)
        {
            result.AddError(
                "Request",
                "REQUIRED",
                "Create project request is required.");

            return result;
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            result.AddError(
                "Name",
                "REQUIRED",
                "Project name is required.");
        }
        else if (request.Name.Trim().Length < 2 ||
                 request.Name.Trim().Length > 150)
        {
            result.AddError(
                "Name",
                "INVALID_LENGTH",
                "Project name must contain 2 to 150 characters.");
        }

        if (string.IsNullOrWhiteSpace(request.ProjectType))
        {
            result.AddError(
                "ProjectType",
                "REQUIRED",
                "Project type is required.");
        }
        else if (request.ProjectType.Length > 100)
        {
            result.AddError(
                "ProjectType",
                "INVALID_LENGTH",
                "Project type must not exceed 100 characters.");
        }

        if (request.LocationReference is not null &&
            request.LocationReference.Length > 500)
        {
            result.AddError(
                "LocationReference",
                "INVALID_LENGTH",
                "Location reference must not exceed 500 characters.");
        }

        return result;
    }
}