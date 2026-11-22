namespace BuildMind.Application.Features.Auth.Abstractions;

public sealed class DuplicateEmailException : Exception
{
    public DuplicateEmailException()
        : base("Account registration conflict.")
    {
    }
}