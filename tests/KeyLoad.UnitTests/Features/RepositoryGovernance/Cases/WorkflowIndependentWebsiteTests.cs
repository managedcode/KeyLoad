using KeyLoad.UnitTests.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.RepositoryGovernance;

internal sealed class WorkflowIndependentWebsiteTests
{
    private const string QualifyAction = ".github/workflows/Features/BenchmarkComparisons/QualifySite/action.yml";
    private const string BuildAction = ".github/workflows/Features/BenchmarkComparisons/BuildIsolatedSite/action.yml";
    private const string DeployAction = ".github/workflows/Features/BenchmarkComparisons/DeploySite/action.yml";

    /// <summary>AC-BC-WEB-001/004: source and producer events independently reach the same website consumer.</summary>
    [Test]
    public async Task AcBcWeb001SourcePublicationDoesNotDependOnBenchmarkJobs()
    {
        var ci = WorkflowLayoutSource.Read("ci.yml");
        var qualify = WorkflowLayoutSource.JobBlock(ci, "qualify");
        await Assert.That(qualify.Contains("needs:", StringComparison.Ordinal)).IsFalse();
        await Assert.That(qualify.Contains("github.event_name == 'push'", StringComparison.Ordinal)).IsTrue();
        await Assert.That(qualify.Contains("github.event_name == 'workflow_run'", StringComparison.Ordinal)).IsTrue();
        await Assert.That(ci.Contains("workflows: [Benchmarks]", StringComparison.Ordinal)).IsTrue();
        await Assert.That(ci.Contains("benchmark_mode: ${{ steps.qualification.outputs.benchmark_mode }}",
            StringComparison.Ordinal)).IsTrue();
    }

    /// <summary>AC-BC-WEB-002/005: explicit absence selects complete content tests while measured input retains the full suite.</summary>
    [Test]
    public async Task AcBcWeb002OptionalCaptureHasAnExplicitClosedContentRoute()
    {
        var action = Read(QualifyAction);
        await Assert.That(action.Contains("\"--optional=true\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains(".result.state == \"unavailable\"", StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains("if: steps.source.outputs.benchmark_mode == 'measured'",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains("--KeyLoadTests:Filter=/*/*/SiteContent*/*", StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains("[[ \"$KEYLOAD_SITE_BENCHMARK_MODE\" == measured ]]",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains("[int]$counts.executed -ne [int]$counts.total",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(action.Contains("continue-on-error", StringComparison.Ordinal)).IsFalse();
    }

    /// <summary>AC-BC-WEB-003/004: no-data output has honest provenance and availability is rechecked before Pages.</summary>
    [Test]
    public async Task AcBcWeb003EmptyPublicationCannotInventOrReuseMeasurements()
    {
        var build = Read(BuildAction);
        var deploy = Read(DeployAction);
        await Assert.That(build.Contains("--benchmarks=none", StringComparison.Ordinal)).IsTrue();
        await Assert.That(build.Contains("measured:null", StringComparison.Ordinal)).IsTrue();
        await Assert.That(build.Contains("benchmarks:null", StringComparison.Ordinal)).IsTrue();
        await Assert.That(build.Contains(".passed == true", StringComparison.Ordinal)).IsTrue();
        await Assert.That(deploy.Contains("--optional=true", StringComparison.Ordinal)).IsTrue();
        await Assert.That(deploy.Contains("metadata-proof.json", StringComparison.Ordinal)).IsTrue();
        var freshness = deploy.IndexOf("site-isolated-github-cli.mjs fresh", StringComparison.Ordinal);
        var publication = deploy.IndexOf("name: Deploy website to GitHub Pages", StringComparison.Ordinal);
        await Assert.That(freshness).IsGreaterThan(-1);
        await Assert.That(freshness).IsLessThan(publication);
        await Assert.That(deploy.Contains("continue-on-error", StringComparison.Ordinal)).IsFalse();
    }

    private static string Read(string path)
        => File.ReadAllText(Path.Combine(IsolatedAggregateNodeProcess.RepositoryRoot(), path));
}
