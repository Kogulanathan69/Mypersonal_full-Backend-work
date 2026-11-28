using BuildMind.Infrastructure.Identity;
using BuildMind.Infrastructure.Persistence.Repositories.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace BuildMind.Infrastructure.Tests.Identity;

public sealed class M1MutationSessionGuardRegistrationTests
{
    [Fact]
    public void AddM1Authentication_registers_guard_once_with_scoped_lifetime()
    {
        var services = new ServiceCollection();
        var options = new JwtOptions
        {
            Issuer = "M1-DI-Test",
            Audience = "M1-DI-Test-Client",
            SigningKey = Convert.ToBase64String(new byte[32])
        };

        services.AddM1Authentication(options);
        services.AddM1Authentication(options);

        var registration = Assert.Single(services, x => x.ServiceType == typeof(M1MutationSessionGuard));
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
        Assert.Equal(typeof(M1MutationSessionGuard), registration.ImplementationType);
    }
}
