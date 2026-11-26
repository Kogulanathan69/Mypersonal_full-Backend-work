using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Mvc.Testing;

namespace BuildMind.Infrastructure.Tests.DependencyInjection;

public sealed class TimeProviderRegistrationTests
{
    [Fact]
    public async Task Api_container_resolves_the_system_time_provider_as_a_singleton()
    {
        await using var factory = new WebApplicationFactory<Program>();

        var first = factory.Services.GetRequiredService<TimeProvider>();
        var second = factory.Services.GetRequiredService<TimeProvider>();

        Assert.Same(TimeProvider.System, first);
        Assert.Same(first, second);
    }

    [Fact]
    public void A_deterministic_provider_registered_first_is_not_replaced()
    {
        var fixedTime = new SyntheticFixedTimeProvider(new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection()
            .AddSingleton<TimeProvider>(fixedTime)
            .AddInfrastructure(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider();

        var resolved = provider.GetRequiredService<TimeProvider>();
        Assert.Same(fixedTime, resolved);
        Assert.Equal(fixedTime.Now, resolved.GetUtcNow());
    }

    /// <summary>Synthetic deterministic clock for tests (no extra package).</summary>
    private sealed class SyntheticFixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
