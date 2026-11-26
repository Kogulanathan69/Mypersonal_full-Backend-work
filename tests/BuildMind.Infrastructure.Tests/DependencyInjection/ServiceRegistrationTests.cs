using BuildMind.Application;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BuildMind.Infrastructure.Tests.DependencyInjection;

public sealed class ServiceRegistrationTests
{
    [Fact]
    public void Layer_registrations_build_a_valid_container()
    {
        var services = new ServiceCollection()
            .AddApplication()
            .AddInfrastructure(new ConfigurationBuilder().Build());

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        Assert.NotNull(provider);
    }

    [Fact]
    public async Task Api_container_passes_build_and_scope_validation_in_production_environment()
    {
        // Development enables validation by default; force it in Production so CI checks the strict path.
        await using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Production");
            builder.UseDefaultServiceProvider(options =>
            {
                options.ValidateOnBuild = true;
                options.ValidateScopes = true;
            });
        });

        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/health");

        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public void AddApplication_rejects_null_services()
    {
        Assert.Throws<ArgumentNullException>(() => BuildMind.Application.DependencyInjection.AddApplication(null!));
    }

    [Fact]
    public void AddInfrastructure_rejects_null_arguments()
    {
        var configuration = new ConfigurationBuilder().Build();

        Assert.Throws<ArgumentNullException>(
            () => BuildMind.Infrastructure.DependencyInjection.AddInfrastructure(null!, configuration));
        Assert.Throws<ArgumentNullException>(
            () => BuildMind.Infrastructure.DependencyInjection.AddInfrastructure(new ServiceCollection(), null!));
    }
}
