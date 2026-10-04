namespace KeyLoad.UnitTests.Features.RepositoryGovernance;

internal sealed class WorkflowLayoutPipelineSourceTests
{
    private const string BenchmarksFile = "benchmarks.yml";
    private const string ReleaseFile = "release.yml";

    [Test]
    public async Task AcPipe002AndBcFail017BenchmarksRetainsOnlyNativeDatabaseGraphAndJsonAggregation()
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
            "comparison-build", "comparison-plan", "comparison-images", "comparison-aggregate"
        }.Concat(WorkflowDatabaseGroups.JobIds).Order(StringComparer.Ordinal).ToArray();
        await Assert.That(jobs.Order(StringComparer.Ordinal).SequenceEqual(expectedJobs)).IsTrue();

        var build = WorkflowLayoutSource.JobBlock(benchmarks, "comparison-build");
        await Assert.That(build.Contains("dotnet build KeyLoad.slnx --no-restore --configuration Release",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(build.Contains("dotnet format KeyLoad.slnx --verify-no-changes --no-restore",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(build.Contains("node scripts/Features/RepositoryGovernance/verify.mjs",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(WorkflowLayoutSource.JobBlock(benchmarks, "comparison-images")
            .Contains("\n    needs:", StringComparison.Ordinal)).IsFalse();
        foreach (var jobId in WorkflowDatabaseGroups.JobIds)
        {
            await AssertNativeJob(benchmarks, jobId);
        }
        await AssertAggregate(WorkflowLayoutSource.JobBlock(benchmarks, "comparison-aggregate"));
        await AssertPinnedImage(WorkflowLayoutSource.JobBlock(benchmarks, "comparison-images"));
        await AssertDatabaseOnly(benchmarks);
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
        await Assert.That(job.Contains(WorkflowDatabaseGroups.AggregateNeeds,
            StringComparison.Ordinal)).IsTrue();
        var steps = WorkflowStepNameTests.StepBlocks(job);
        var expected = new[]
        {
            "name: Verify benchmark plan", "name: Download benchmark results",
            "name: Check all 270 benchmark results", "name: Save combined benchmark results",
            "name: Save GitHub result verification"
        };
        var authenticatedSteps = steps.Where(static step => !step.Contains("name: Download source code", StringComparison.Ordinal));
        await Assert.That(authenticatedSteps.Select(static step => step.Split('\n')[0].Trim()[2..])
            .SequenceEqual(expected)).IsTrue();
        await Assert.That(job.Contains("name: comparison-isolated-suite", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("name: comparison-isolated-provider-evidence", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertPinnedImage(string job)
    {
        await Assert.That(job.Contains("\n    needs:", StringComparison.Ordinal)).IsFalse();
        await Assert.That(job.Contains("TimeSeriesIntensivePinnedImageTests", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("timeseries-native-pinned-image-facts", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertDatabaseOnly(string workflow)
    {
        foreach (var excluded in new[]
        {
            "website", "Chrome", "browser", "pages:", "id-token:", "QualifySite", "DeploySite",
            "BuildIsolatedSite", "SiteQualificationStartupTests", "site/", "site-isolated-", "Suite=site"
        })
        {
            await Assert.That(workflow.Contains(excluded, StringComparison.OrdinalIgnoreCase)).IsFalse();
        }
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
        var firstWrite = job.IndexOf("Create release tag", StringComparison.Ordinal);
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
