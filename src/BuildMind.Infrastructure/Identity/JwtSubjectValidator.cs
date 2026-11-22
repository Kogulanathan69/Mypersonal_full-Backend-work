using System.Security.Claims;

namespace BuildMind.Infrastructure.Identity;

public static class JwtSubjectValidator
{
    public static bool IsValid(ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true)
            return false;

        var subjects = principal.FindAll("sub").ToArray();

        return subjects.Length == 1
            && Guid.TryParse(subjects[0].Value, out var userId)
            && userId != Guid.Empty;
    }
}