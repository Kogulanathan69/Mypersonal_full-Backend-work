namespace BuildMind.Application.Features.Auth.Abstractions;

public interface IPasswordHashService
{
    string HashPassword(string password);

    bool VerifyPassword(
        string password,
        string passwordHash);
}