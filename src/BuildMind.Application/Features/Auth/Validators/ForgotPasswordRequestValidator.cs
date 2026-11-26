using EmailAddressAttribute = System.ComponentModel.DataAnnotations.EmailAddressAttribute;
using BuildMind.Application.Common.Validation;
using BuildMind.Application.Features.Auth.DTOs;

namespace BuildMind.Application.Features.Auth.Validators;

public sealed class ForgotPasswordRequestValidator
    : IValidator<ForgotPasswordRequest>
{
    public ValidationResult Validate(ForgotPasswordRequest request)
    {
        var result = new ValidationResult();

        if (request is null)
        {
            result.AddError(
                "Request",
                "REQUIRED",
                "Forgot password request is required.");

            return result;
        }

        if (string.IsNullOrWhiteSpace(request.Email))
        {
            result.AddError(
                "Email",
                "REQUIRED",
                "Email is required.");
        }
        else if (request.Email.Length > 254 ||
                 !new EmailAddressAttribute().IsValid(request.Email))
        {
            result.AddError(
                "Email",
                "INVALID_EMAIL",
                "Enter a valid email address.");
        }

        return result;
    }
}