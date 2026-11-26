using EmailAddressAttribute = System.ComponentModel.DataAnnotations.EmailAddressAttribute;
using BuildMind.Application.Common.Validation;
using BuildMind.Application.Features.Auth.DTOs;

namespace BuildMind.Application.Features.Auth.Validators;

public sealed class LoginRequestValidator
    : IValidator<LoginRequest>
{
    public ValidationResult Validate(LoginRequest request)
    {
        var result = new ValidationResult();

        if (request is null)
        {
            result.AddError(
                "Request",
                "REQUIRED",
                "Login request is required.");

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
        else if (request.Password.Length > 128)
        {
            result.AddError(
                "Password",
                "INVALID_LENGTH",
                "Password exceeds the maximum allowed length.");
        }

        return result;
    }
}