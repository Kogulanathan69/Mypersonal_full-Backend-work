using BuildMind.Application.Features.PlanEditor.Integration;
using BuildMind.Application.Features.Projects.Abstractions;
using BuildMind.Infrastructure.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BuildMind.Infrastructure.Tests.Identity;

/// <summary>
/// M1-M3-AUTH-V1.0 decision 5: the Option A port stays unregistered (disabled by default) in every current composition.
/// The version store is available only where M1 projects are composed (authentication + database, Development).
/// </summary>
public sealed class ProjectAuthorizationCompositionTests
{
    [Fact]
    public void Default_configuration_registers_neither_the_port_nor_the_version_store()
    {
        using var factory = new BuildMindApiFactory();
        _ = factory.CreateClient();
        var services = factory.Services.GetRequiredService<IServiceProviderIsService>();

        Assert.False(services.IsService(typeof(IProjectAuthorizationPort)));
        Assert.False(services.IsService(typeof(IProjectAuthorizationVersionStore)));
    }

    [Fact]
    public void Development_sign_in_registers_the_version_store_but_not_the_port()
    {
        using var factory = M1ApiIntegrationTests.CreateFactory(new InMemoryM1Store());
        _ = factory.CreateClient();
        var services = factory.Services.GetRequiredService<IServiceProviderIsService>();

        Assert.False(services.IsService(typeof(IProjectAuthorizationPort)));
        Assert.True(services.IsService(typeof(IProjectAuthorizationVersionStore)));
    }
}
