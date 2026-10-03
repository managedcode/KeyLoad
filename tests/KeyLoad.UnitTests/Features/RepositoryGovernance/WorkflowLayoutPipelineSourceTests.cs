namespace KeyLoad.UnitTests.Features.RepositoryGovernance;

internal sealed class WorkflowLayoutPipelineSourceTests
{
    private const string BenchmarksFile = "benchmarks.yml";
    private const string ReleaseFile = "release.yml";
    private const string QualifySiteAction = "./control/.github/workflows/Features/BenchmarkComparisons/QualifySite";
    private const string DeploySiteAction = "./control/.github/workflows/Features/BenchmarkComparisons/DeploySite";

    [Test]
    public async Task AcPipe002BenchmarksRetainsNativeGraphAndGatesWebsiteAfterFullQualification()
    {
        var benchmarks = WorkflowLayoutSource.Read(BenchmarksFile);
        await Assert.That(benchmarks.StartsWith("name: Benchmarks\n", StringComparison.Ordinal)).IsTrue();
        var events = WorkflowLayoutSource.EventBlock(benchmarks);
        await Assert.That(events.Contains("push:\n    branches: [main]", StringComparison.Ordinal)).IsTrue();
        await Assert.That(events.Contains("workflow_dispatch:", StringComparison.Ordinal)).IsTrue();
        await Assert.That(events.Contains("pull_request:", StringComparison.Ordinal)).IsFalse();
        var jobs = WorkflowLayoutSource.JobIds(benchmarks);
        var expectedJobs = new[]
        {
            "comparison-build", "comparison-plan", "comparison-images", "comparison-preflight",
            "comparison-crud", "comparison-specialized", "comparison-aggregate", "timeseries-image-facts",
            "qualify", "deploy",
        }.Order(StringComparer.Ordinal).ToArray();
        await Assert.That(jobs.Order(StringComparer.Ordinal).SequenceEqual(expectedJobs)).IsTrue();

        var build = WorkflowLayoutSource.JobBlock(benchmarks, "comparison-build");
        await Assert.That(build.Contains("dotnet build KeyLoad.slnx --no-restore --configuration Release",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(build.Contains("dotnet format KeyLoad.slnx --verify-no-changes --no-restore",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(build.Contains("node scripts/Features/RepositoryGovernance/verify.mjs",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(WorkflowLayoutSource.JobBlock(benchmarks, "comparison-images")
            .Contains("needs: [comparison-build, comparison-plan]", StringComparison.Ordinal)).IsTrue();
        await AssertNativeJob(benchmarks, "comparison-preflight");
        await AssertNativeJob(benchmarks, "comparison-crud");
        await AssertNativeJob(benchmarks, "comparison-specialized");
        await AssertAggregate(WorkflowLayoutSource.JobBlock(benchmarks, "comparison-aggregate"));
        await AssertPinnedImage(WorkflowLayoutSource.JobBlock(benchmarks, "timeseries-image-facts"));
        await AssertWebsiteDependency(benchmarks);
    }

    [Test]
    public async Task AcRel001To003ReleaseIsManualOwnMainAndPublishesOnlyVerifiedDatedAssets()
    {
        var release = WorkflowLayoutSource.Read(ReleaseFile);
        await Assert.That(release.StartsWith("name: Release\n", StringComparison.Ordinal)).IsTrue();
        var events = WorkflowLayoutSource.EventBlock(release);
        await Assert.That(events.Contains("workflow_dispatch:", StringComparison.Ordinal)).IsTrue();
        await Assert.That(events.Contains("push:", StringComparison.Ordinal)).IsFalse();
        await Assert.That(events.Contains("pull_request:", StringComparison.Ordinal)).IsFalse();
        var jobs = WorkflowLayoutSource.JobIds(release);
        await Assert.That(jobs.Order(StringComparer.Ordinal).SequenceEqual(new[] { "build", "publish", "version" }))
            .IsTrue();

        await AssertReleaseVersion(WorkflowLayoutSource.JobBlock(release, "version"));
        await AssertReleaseBuild(WorkflowLayoutSource.JobBlock(release, "build"));
        await AssertReleasePublication(WorkflowLayoutSource.JobBlock(release, "publish"));
    }

    private static async Task AssertNativeJob(string workflow, string jobId)
    {
        var job = WorkflowLayoutSource.JobBlock(workflow, jobId);
        await Assert.That(job.Contains("runs-on: ubuntu-latest", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("IsolatedNativeComparisonTests", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertAggregate(string job)
    {
        await Assert.That(job.Contains("needs: [comparison-plan, comparison-images, comparison-crud, comparison-specialized]",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("comparison-crud, comparison-specialized", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("--isolated=", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertPinnedImage(string job)
    {
        await Assert.That(job.Contains("needs: comparison-build", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("TimeSeriesIntensivePinnedImageTests", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("timeseries-native-pinned-image-facts", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertWebsiteDependency(string workflow)
    {
        var qualify = WorkflowLayoutSource.JobBlock(workflow, "qualify");
        await Assert.That(qualify.Contains("needs: [comparison-aggregate, timeseries-image-facts]",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(qualify.Contains(QualifySiteAction, StringComparison.Ordinal)).IsTrue();
        var deploy = WorkflowLayoutSource.JobBlock(workflow, "deploy");
        await Assert.That(deploy.Contains("needs: qualify", StringComparison.Ordinal)).IsTrue();
        await Assert.That(deploy.Contains(DeploySiteAction, StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertReleaseVersion(string job)
    {
        await Assert.That(job.Contains("refs/heads/main", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("release-github-context.py", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("release-version-cli.mjs", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("--reservation=", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("package_version: ${{ steps.reserve.outputs.package_version }}",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("release-version-${{ github.run_id }}", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("actions/upload-artifact", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("GITHUB_RUN_ATTEMPT", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertReleaseBuild(string job)
    {
        await Assert.That(job.Contains("needs: version", StringComparison.Ordinal)).IsTrue();
        foreach (var required in new[]
        {
            "node scripts/Features/RepositoryGovernance/verify.mjs",
            "dotnet build KeyLoad.slnx", "dotnet format KeyLoad.slnx", "dotnet pack KeyLoad.slnx",
            "dotnet publish src/KeyLoad.Server", "dotnet publish src/KeyLoad.Cli", "compose.yml",
            "docker build", "docker save", "release-manifest.json", "RELEASE_PACKAGE_VERSION",
            "-p:PackageVersion=\"$RELEASE_PACKAGE_VERSION\"",
            "jq -e --arg expected \"$RELEASE_PACKAGE_VERSION\" '.packageVersion == $expected'",
        })
        {
            await Assert.That(job.Contains(required, StringComparison.Ordinal)).IsTrue();
        }

        await Assert.That(job.Contains("release-assets.py", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("actions/upload-artifact", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertReleasePublication(string job)
    {
        await Assert.That(job.Contains("needs: [version, build]", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("contents: write", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("packages: write", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("actions: read", StringComparison.Ordinal)).IsTrue();
        var ciProof = job.IndexOf("release-github-context.py ci", StringComparison.Ordinal);
        var firstWrite = job.IndexOf("Create or verify the immutable annotated source tag", StringComparison.Ordinal);
        await Assert.That(ciProof).IsGreaterThan(-1);
        await Assert.That(ciProof < firstWrite).IsTrue();
        await Assert.That(job.Contains("sha256sum --check", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("git tag --annotate", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("git -c credential.helper='!gh auth git-credential' push origin \"refs/tags/$RELEASE_TAG\"",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("docker push", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("gh release create", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("ghcr.io", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("releases/assets/$id", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("nuget push", StringComparison.OrdinalIgnoreCase)).IsFalse();
        await Assert.That(job.Contains("--force", StringComparison.Ordinal)).IsFalse();
    }
}
