using EmailAddressAttribute = System.ComponentModel.DataAnnotations.EmailAddressAttribute;
using BuildMind.Application.Common.Validation;
using BuildMind.Application.Features.Auth.DTOs;

namespace BuildMind.Application.Features.Auth.Validators;

public sealed class RegisterRequestValidator
    : IValidator<RegisterRequest>
{
    public ValidationResult Validate(RegisterRequest request)
    {
        var result = new ValidationResult();

        if (request is null)
        {
            result.AddError(
                "Request",
                "REQUIRED",
                "Registration request is required.");

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

        if (string.IsNullOrWhiteSpace(request.Password))
        {
            result.AddError(
                "Password",
                "REQUIRED",
                "Password is required.");
        }
        else if (request.Password.Length < 15 ||
                 request.Password.Length > 128)
        {
            result.AddError(
                "Password",
                "INVALID_LENGTH",
                "Password must contain 15 to 128 characters.");
        }

        return result;
    }
}