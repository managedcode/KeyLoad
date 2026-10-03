namespace KeyLoad.UnitTests.Features.RepositoryGovernance;

internal sealed class WorkflowLayoutPipelineSourceTests
{
    private const string BenchmarksFile = "benchmarks.yml";
    private const string ReleaseFile = "release.yml";
    private const string PagesFile = "pages.yml";

    [Test]
    public async Task AcWf002BenchmarksRetainsTheIsolatedAndPinnedImageGraph()
    {
        var benchmarks = WorkflowLayoutSource.Read(BenchmarksFile);
        await Assert.That(benchmarks.StartsWith("name: Benchmarks\n", StringComparison.Ordinal)).IsTrue();
        var jobs = WorkflowLayoutSource.JobIds(benchmarks);
        foreach (var required in new[]
        {
            "comparison-build", "comparison-plan", "comparison-images", "comparison-preflight",
            "comparison-crud", "comparison-specialized", "comparison-aggregate", "timeseries-image-facts",
        })
        {
            await Assert.That(jobs.Contains(required, StringComparer.Ordinal)).IsTrue();
        }

        var build = WorkflowLayoutSource.JobBlock(benchmarks, "comparison-build");
        await Assert.That(build.Contains("dotnet build KeyLoad.slnx --no-restore --configuration Release",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(build.Contains("dotnet format KeyLoad.slnx --verify-no-changes --no-restore",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(build.Contains("node scripts/Features/RepositoryGovernance/verify.mjs",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(WorkflowLayoutSource.JobBlock(benchmarks, "comparison-images")
            .Contains("needs: [comparison-build, comparison-plan]", StringComparison.Ordinal)).IsTrue();
        await AssertNativeJob(benchmarks, "comparison-preflight", "IsolatedNativeComparisonTests");
        await AssertNativeJob(benchmarks, "comparison-crud", "IsolatedNativeComparisonTests");
        await AssertNativeJob(benchmarks, "comparison-specialized", "IsolatedNativeComparisonTests");
        var aggregate = WorkflowLayoutSource.JobBlock(benchmarks, "comparison-aggregate");
        await Assert.That(aggregate.Contains("comparison-crud, comparison-specialized", StringComparison.Ordinal)).IsTrue();
        await Assert.That(aggregate.Contains("--isolated=", StringComparison.Ordinal)).IsTrue();
        await AssertPinnedImage(WorkflowLayoutSource.JobBlock(benchmarks, "timeseries-image-facts"));
    }

    [Test]
    public async Task AcWf005ReleaseBuildsAndRetainsNuGetPackagesWithoutPublishing()
    {
        var release = WorkflowLayoutSource.Read(ReleaseFile);
        await Assert.That(release.StartsWith("name: Release\n", StringComparison.Ordinal)).IsTrue();
        var events = WorkflowLayoutSource.EventBlock(release);
        await Assert.That(events.Contains("workflow_dispatch:", StringComparison.Ordinal)).IsTrue();
        await Assert.That(events.Contains("tags:", StringComparison.Ordinal)).IsTrue();
        var allJobs = WorkflowLayoutSource.JobIds(release);
        await Assert.That(allJobs.Length > 0).IsTrue();
        var pack = string.Join('\n', allJobs.Select(job => WorkflowLayoutSource.JobBlock(release, job)));
        foreach (var required in new[]
        {
            "dotnet build KeyLoad.slnx --no-restore --configuration Release",
            "dotnet format KeyLoad.slnx --verify-no-changes --no-restore",
            "node scripts/Features/RepositoryGovernance/verify.mjs",
            "dotnet pack",
            ".nupkg",
            "actions/upload-artifact",
        })
        {
            await Assert.That(pack.Contains(required, StringComparison.Ordinal)).IsTrue();
        }

        await Assert.That(pack.Contains("nuget push", StringComparison.OrdinalIgnoreCase)).IsFalse();
    }

    [Test]
    public async Task AcWf004WebsiteFollowsBenchmarks()
    {
        var pages = WorkflowLayoutSource.Read(PagesFile);
        await Assert.That(pages.StartsWith("name: Website\n", StringComparison.Ordinal)).IsTrue();
        await Assert.That(pages.Contains("workflows: [Benchmarks]", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertNativeJob(string workflow, string jobId, string testName)
    {
        var job = WorkflowLayoutSource.JobBlock(workflow, jobId);
        await Assert.That(job.Contains("runs-on: ubuntu-latest", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains(testName, StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertPinnedImage(string job)
    {
        await Assert.That(job.Contains("needs: comparison-build", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("TimeSeriesIntensivePinnedImageTests", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("timeseries-native-pinned-image-facts", StringComparison.Ordinal)).IsTrue();
    }
}
