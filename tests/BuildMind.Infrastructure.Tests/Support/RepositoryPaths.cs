namespace BuildMind.Infrastructure.Tests.Support;

/// <summary>Locates the repository root (the folder containing BuildMind.sln) from the test output directory.</summary>
internal static class RepositoryPaths
{
    public const string SolutionFileName = "BuildMind.sln";

    public static string Root { get; } = FindRoot();

    public static string SolutionFile => Path.Combine(Root, SolutionFileName);

    public static string ProjectFile(string projectName) =>
        Path.Combine(Root, "src", projectName, projectName + ".csproj");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, SolutionFileName)))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException(
            $"Could not find {SolutionFileName} above {AppContext.BaseDirectory}.");
    }
}
