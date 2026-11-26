using EmailAddressAttribute = System.ComponentModel.DataAnnotations.EmailAddressAttribute;
using BuildMind.Application.Common.Validation;
using BuildMind.Application.Features.Auth.DTOs;

namespace BuildMind.Application.Features.Auth.Validators;

public sealed class ResetPasswordRequestValidator
    : IValidator<ResetPasswordRequest>
{
    public ValidationResult Validate(ResetPasswordRequest request)
    {
        var result = new ValidationResult();

        if (request is null)
        {
            result.AddError(
                "Request",
                "REQUIRED",
                "Reset password request is required.");

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

        if (string.IsNullOrWhiteSpace(request.Token))
        {
            result.AddError(
                "Token",
                "REQUIRED",
                "Reset token is required.");
        }

        if (string.IsNullOrWhiteSpace(request.NewPassword))
        {
            result.AddError(
                "NewPassword",
                "REQUIRED",
                "New password is required.");
        }
        else if (request.NewPassword.Length < 15 ||
                 request.NewPassword.Length > 128)
        {
            result.AddError(
                "NewPassword",
                "INVALID_LENGTH",
                "Password must contain 15 to 128 characters.");
        }

        if (string.IsNullOrWhiteSpace(request.ConfirmPassword))
        {
            result.AddError(
                "ConfirmPassword",
                "REQUIRED",
                "Confirm password is required.");
        }
        else if (!string.Equals(
                     request.NewPassword,
                     request.ConfirmPassword,
                     StringComparison.Ordinal))
        {
            result.AddError(
                "ConfirmPassword",
                "PASSWORD_MISMATCH",
                "Passwords do not match.");
        }

        return result;
    }
}