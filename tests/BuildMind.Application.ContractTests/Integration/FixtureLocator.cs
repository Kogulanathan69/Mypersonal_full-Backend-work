namespace BuildMind.Application.ContractTests.Integration;

internal static class FixtureLocator
{
    public static string ResolveGeometryFixture(string[] arguments)
    {
        if (arguments.Length > 1)
            throw new ArgumentException("Expected zero arguments or one explicit geometry fixture path.");

        if (arguments.Length == 1)
            return RequireFile(Path.GetFullPath(arguments[0]), "explicit geometry fixture");

        const string relativePath = "docs/proposals/m3-plan-editor/PLAN-GEOMETRY-SAMPLE.json";
        var searched = new List<string>();
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.GetFullPath(Path.Combine(directory.FullName, relativePath));
                if (!searched.Contains(candidate, StringComparer.OrdinalIgnoreCase)) searched.Add(candidate);
                if (File.Exists(candidate)) return candidate;
            }
        }

        throw new FileNotFoundException(
            $"Could not locate {relativePath}. Searched:{Environment.NewLine}{string.Join(Environment.NewLine, searched)}");
    }

    public static string ResolveCommandFixture(string geometryFixture) => RequireFile(
        Path.Combine(Path.GetDirectoryName(geometryFixture)!, "EDIT-COMMAND-SAMPLES.json"),
        "command fixture adjacent to the geometry fixture");

    public static string ResolveRepositoryFile(string relativePath)
    {
        foreach (var start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.GetFullPath(Path.Combine(directory.FullName, relativePath));
                if (File.Exists(candidate)) return candidate;
            }
        }
        throw new FileNotFoundException($"Could not locate repository fixture: {relativePath}");
    }

    private static string RequireFile(string path, string description)
    {
        if (!File.Exists(path)) throw new FileNotFoundException($"Could not locate {description}: {path}", path);
        return path;
    }
}
