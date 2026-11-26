using BuildMind.Application.Common.Validation;
using BuildMind.Application.Features.Projects.DTOs;

namespace BuildMind.Application.Features.Projects.Validators;

public sealed class UpdateProjectRequestValidator
    : IValidator<UpdateProjectRequest>
{
    public ValidationResult Validate(UpdateProjectRequest request)
    {
        var result = new ValidationResult();

        if (request is null)
        {
            result.AddError(
                "Request",
                "REQUIRED",
                "Update project request is required.");

            return result;
        }

        if (request.Name is not null)
        {
            if (string.IsNullOrWhiteSpace(request.Name) ||
                request.Name.Trim().Length < 2 ||
                request.Name.Trim().Length > 150)
            {
                result.AddError(
                    "Name",
                    "INVALID_NAME",
                    "Project name must contain 2 to 150 characters.");
            }
        }

        if (request.ProjectType is not null)
        {
            if (string.IsNullOrWhiteSpace(request.ProjectType) ||
                request.ProjectType.Length > 100)
            {
                result.AddError(
                    "ProjectType",
                    "INVALID_PROJECT_TYPE",
                    "Enter a valid project type.");
            }
        }

        if (request.LocationReference is not null &&
            request.LocationReference.Length > 500)
        {
            result.AddError(
                "LocationReference",
                "INVALID_LENGTH",
                "Location reference must not exceed 500 characters.");
        }

        if (request.Revision < 1)
        {
            result.AddError(
                "Revision",
                "INVALID_REVISION",
                "Revision must be greater than zero.");
        }

        if (request.Name is null &&
            request.ProjectType is null &&
            request.LocationReference is null)
        {
            result.AddError(
                "Request",
                "NO_CHANGES",
                "At least one project field must be provided.");
        }

        return result;
    }
}