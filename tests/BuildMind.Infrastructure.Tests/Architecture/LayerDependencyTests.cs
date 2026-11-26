using System.Reflection;
using System.Xml.Linq;
using BuildMind.Infrastructure.Tests.Support;

namespace BuildMind.Infrastructure.Tests.Architecture;

/// <summary>
/// Enforces Clean Architecture dependency direction at two levels:
/// declared project references (csproj) and compiled assembly references.
/// Compiled references alone are insufficient because the compiler omits unused references.
/// </summary>
public sealed class LayerDependencyTests
{
    private const string Domain = "BuildMind.Domain";
    private const string Application = "BuildMind.Application";
    private const string Infrastructure = "BuildMind.Infrastructure";
    private const string Api = "BuildMind.API";

    private static readonly string[] SourceProjects = [Domain, Application, Infrastructure, Api];

    /// <summary>Allowed inward references for each layer. API is the only composition root.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> AllowedReferences = new Dictionary<string, string[]>
    {
        [Domain] = [],
        [Application] = [Domain],
        [Infrastructure] = [Application, Domain],
        [Api] = [Application, Infrastructure, Domain],
    };

    private static readonly IReadOnlyDictionary<string, Assembly> Assemblies = new Dictionary<string, Assembly>
    {
        [Domain] = typeof(BuildMind.Domain.Entities.Project).Assembly,
        [Application] = typeof(BuildMind.Application.DependencyInjection).Assembly,
        [Infrastructure] = typeof(BuildMind.Infrastructure.DependencyInjection).Assembly,
        [Api] = typeof(Program).Assembly,
    };

    [Theory]
    [InlineData(Domain)]
    [InlineData(Application)]
    [InlineData(Infrastructure)]
    [InlineData(Api)]
    public void Declared_project_references_follow_dependency_direction(string project)
    {
        var disallowed = DeclaredProjectReferences(project).Except(AllowedReferences[project]).ToArray();

        Assert.True(disallowed.Length == 0, $"{project} must not reference: {string.Join(", ", disallowed)}");
    }

    [Theory]
    [InlineData(Domain)]
    [InlineData(Application)]
    [InlineData(Infrastructure)]
    [InlineData(Api)]
    public void Compiled_assembly_references_follow_dependency_direction(string project)
    {
        var disallowed = Assemblies[project]
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => SourceProjects.Contains(name))
            .Except(AllowedReferences[project])
            .ToArray();

        Assert.True(disallowed.Length == 0, $"{project} assembly must not reference: {string.Join(", ", disallowed)}");
    }

    [Fact]
    public void Domain_has_no_project_references()
    {
        Assert.Empty(DeclaredProjectReferences(Domain));
    }

    [Fact]
    public void Application_does_not_reference_infrastructure_or_api()
    {
        var references = DeclaredProjectReferences(Application);

        Assert.DoesNotContain(Infrastructure, references);
        Assert.DoesNotContain(Api, references);
    }

    [Fact]
    public void Only_api_composition_root_references_infrastructure()
    {
        var referencingInfrastructure = SourceProjects
            .Where(p => DeclaredProjectReferences(p).Contains(Infrastructure))
            .ToArray();

        Assert.Equal(new[] { Api }, referencingInfrastructure);
    }

    [Fact]
    public void Project_reference_graph_has_no_cycles()
    {
        var graph = SourceProjects.ToDictionary(p => p, DeclaredProjectReferences);
        var state = new Dictionary<string, int>(); // 0 = unvisited, 1 = visiting, 2 = done

        bool HasCycle(string node)
        {
            state[node] = 1;
            foreach (var next in graph.GetValueOrDefault(node, []))
            {
                var nextState = state.GetValueOrDefault(next);
                if (nextState == 1 || (nextState == 0 && HasCycle(next)))
                {
                    return true;
                }
            }

            state[node] = 2;
            return false;
        }

        Assert.DoesNotContain(SourceProjects, p => state.GetValueOrDefault(p) == 0 && HasCycle(p));
    }

    [Fact]
    public void Solution_registers_all_source_projects_and_this_test_project()
    {
        var solution = File.ReadAllText(RepositoryPaths.SolutionFile);

        foreach (var project in SourceProjects)
        {
            Assert.Contains($"{project}.csproj", solution, StringComparison.Ordinal);
        }

        Assert.Contains("BuildMind.Infrastructure.Tests.csproj", solution, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Domain)]
    [InlineData(Application)]
    public void Inner_layers_do_not_reference_aspnetcore(string project)
    {
        var aspNetReferences = Assemblies[project]
            .GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
            .ToArray();

        Assert.True(aspNetReferences.Length == 0, $"{project} must not reference: {string.Join(", ", aspNetReferences)}");

        var frameworkReferences = XDocument.Load(RepositoryPaths.ProjectFile(project))
            .Descendants()
            .Where(e => e.Name.LocalName == "FrameworkReference")
            .Select(e => (string?)e.Attribute("Include"))
            .ToArray();

        Assert.DoesNotContain("Microsoft.AspNetCore.App", frameworkReferences);
    }

    [Theory]
    [InlineData(Domain)]
    [InlineData(Application)]
    public void Inner_layers_do_not_reference_entity_framework_or_mysql(string project)
    {
        static bool IsPersistence(string name) =>
            name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
            || name.StartsWith("MySql", StringComparison.OrdinalIgnoreCase);

        var compiled = Assemblies[project].GetReferencedAssemblies().Select(a => a.Name!).Where(IsPersistence).ToArray();
        var declared = PackageReferences(project).Where(IsPersistence).ToArray();

        Assert.True(compiled.Length == 0, $"{project} assembly must not reference: {string.Join(", ", compiled)}");
        Assert.True(declared.Length == 0, $"{project} must not declare: {string.Join(", ", declared)}");
    }

    [Fact]
    public void Persistence_packages_are_declared_only_where_approved()
    {
        Assert.Contains("MySql.EntityFrameworkCore", PackageReferences(Infrastructure));
        Assert.DoesNotContain("MySql.EntityFrameworkCore", PackageReferences(Api));
        Assert.DoesNotContain("Microsoft.EntityFrameworkCore.Design", PackageReferences(Infrastructure));

        var design = XDocument.Load(RepositoryPaths.ProjectFile(Api))
            .Descendants()
            .Single(e => e.Name.LocalName == "PackageReference"
                && (string?)e.Attribute("Include") == "Microsoft.EntityFrameworkCore.Design");
        var privateAssets = (string?)design.Attribute("PrivateAssets")
            ?? design.Elements().FirstOrDefault(e => e.Name.LocalName == "PrivateAssets")?.Value;

        Assert.Equal("all", privateAssets?.Trim());
    }

    private static string[] PackageReferences(string project) =>
        XDocument.Load(RepositoryPaths.ProjectFile(project))
            .Descendants()
            .Where(e => e.Name.LocalName == "PackageReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => include!)
            .ToArray();

    private static string[] DeclaredProjectReferences(string project)
    {
        var document = XDocument.Load(RepositoryPaths.ProjectFile(project));

        return document
            .Descendants()
            .Where(e => e.Name.LocalName == "ProjectReference")
            .Select(e => (string?)e.Attribute("Include"))
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', '/')))
            .ToArray();
    }
}
