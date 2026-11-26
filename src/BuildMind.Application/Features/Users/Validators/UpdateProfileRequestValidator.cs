using System.Text.RegularExpressions;
using BuildMind.Application.Common.Validation;
using BuildMind.Application.Features.Users.DTOs;

namespace BuildMind.Application.Features.Users.Validators;

public sealed class UpdateProfileRequestValidator
    : IValidator<UpdateProfileRequest>
{
    public ValidationResult Validate(UpdateProfileRequest request)
    {
        var result = new ValidationResult();

        if (request is null)
        {
            result.AddError(
                "Request",
                "REQUIRED",
                "Profile update request is required.");

            return result;
        }

        if (string.IsNullOrWhiteSpace(request.FullName))
        {
            result.AddError(
                "FullName",
                "REQUIRED",
                "Full name is required.");
        }
        else if (request.FullName.Trim().Length < 2 ||
                 request.FullName.Trim().Length > 100)
        {
            result.AddError(
                "FullName",
                "INVALID_LENGTH",
                "Full name must contain 2 to 100 characters.");
        }

        if (request.PhoneNumber is not null)
        {
            if (request.PhoneNumber.Length > 16 ||
                !Regex.IsMatch(
                    request.PhoneNumber,
                    @"\A\+?[0-9]{9,15}\z"))
            {
                result.AddError(
                    "PhoneNumber",
                    "INVALID_PHONE_NUMBER",
                    "Enter a valid phone number.");
            }
        }

        return result;
    }
}