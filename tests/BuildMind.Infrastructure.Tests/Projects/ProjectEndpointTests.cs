using BuildMind.API.Routing;
using Xunit;

namespace BuildMind.Infrastructure.Tests.Projects;

public sealed class ProjectEndpointTests
{
    [Fact]
    public void ProjectEndpointModule_ImplementsApiEndpointModule()
    {
        Assert.IsAssignableFrom<IApiEndpointModule>(new ProjectEndpointModule());
    }
}
