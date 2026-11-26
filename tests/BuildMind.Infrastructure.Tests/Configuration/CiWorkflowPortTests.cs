using System.Text.RegularExpressions;
using BuildMind.Infrastructure.Tests.Support;

namespace BuildMind.Infrastructure.Tests.Configuration;

/// <summary>
/// Regression for the PR #41 CI defect: the synthetic acceptance service and the full-stack smoke API both bound
/// 127.0.0.1:8097, so the smoke test talked to the wrong process. Every loopback listener started by the CI workflow
/// must own a distinct port, and every loopback URL the workflow hands to a test must point at a declared listener.
/// </summary>
public sealed partial class CiWorkflowPortTests
{
    private static readonly string Workflow = File.ReadAllText(Path.Combine(RepositoryPaths.Root, ".github", "workflows", "backend-ci.yml"));

    [GeneratedRegex(@"--host 127\.0\.0\.1 --port (\d{2,5})")]
    private static partial Regex UvicornListener();

    [GeneratedRegex(@"API_PORT: '(\d{2,5})'")]
    private static partial Regex ApiListener();

    [GeneratedRegex(@"http://127\.0\.0\.1:(\d{2,5})")]
    private static partial Regex LoopbackUrl();

    private static List<int> Listeners() =>
        [.. UvicornListener().Matches(Workflow).Concat(ApiListener().Matches(Workflow)).Select(m => int.Parse(m.Groups[1].Value))];

    [Fact]
    public void Every_loopback_listener_owns_a_distinct_port()
    {
        var listeners = Listeners();

        Assert.True(listeners.Count >= 4, $"expected python, rag, acceptance and smoke API listeners, found {listeners.Count}");
        Assert.Equal(listeners.Count, listeners.Distinct().Count());
    }

    [Fact]
    public void Every_loopback_url_points_at_a_declared_listener()
    {
        var listeners = Listeners().ToHashSet();

        var undeclared = LoopbackUrl().Matches(Workflow).Select(m => int.Parse(m.Groups[1].Value)).Where(p => !listeners.Contains(p)).ToList();

        Assert.Empty(undeclared);
    }

    [Fact]
    public void Acceptance_service_and_smoke_api_use_their_assigned_ports()
    {
        Assert.Contains("--app-dir tests --host 127.0.0.1 --port 8094", Workflow, StringComparison.Ordinal);
        Assert.Contains("BUILDMIND_TEST_RAG_ACCEPTANCE_URL: http://127.0.0.1:8094", Workflow, StringComparison.Ordinal);
        Assert.Contains("API_PORT: '8097'", Workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("127.0.0.1:8097/scenario", Workflow, StringComparison.Ordinal);
    }
}
