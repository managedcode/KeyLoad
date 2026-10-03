namespace KeyLoad.UnitTests.Features.RepositoryGovernance;

internal sealed class WorkflowLayoutCiSourceTests
{
    private const string CiFile = "ci.yml";
    private const string TestsFile = "tests.yml";
    private const string BenchmarksFile = "benchmarks.yml";
    private const string ReleaseFile = "release.yml";
    private const string PagesFile = "pages.yml";

    [Test]
    public async Task AcWf001OnlyFiveWorkflowsRemainWithSeparateCiAndTestGates()
    {
        var expected = new[] { CiFile, TestsFile, BenchmarksFile, ReleaseFile, PagesFile }
            .Order(StringComparer.Ordinal).ToArray();
        await Assert.That(WorkflowLayoutSource.TopLevelWorkflowFiles().SequenceEqual(expected)).IsTrue();

        var ci = WorkflowLayoutSource.Read(CiFile);
        await Assert.That(ci.StartsWith("name: CI\n", StringComparison.Ordinal)).IsTrue();
        var ciEvents = WorkflowLayoutSource.EventBlock(ci);
        await Assert.That(ciEvents.Contains("pull_request:", StringComparison.Ordinal)).IsTrue();
        await Assert.That(ciEvents.Contains("workflow_dispatch:", StringComparison.Ordinal)).IsTrue();
        await Assert.That(ciEvents.Contains("push:", StringComparison.Ordinal)).IsFalse();
        var ciJobs = WorkflowLayoutSource.JobIds(ci);
        await Assert.That(ciJobs.Contains("repository-checks", StringComparer.Ordinal)).IsTrue();
        await Assert.That(ciJobs.Contains("verify", StringComparer.Ordinal)).IsTrue();
        await Assert.That(ciJobs.Contains("analyzer-rules", StringComparer.Ordinal)).IsTrue();
        await Assert.That(ciJobs.Any(IsComparisonJob)).IsFalse();
        var ciRules = WorkflowLayoutSource.JobBlock(ci, "repository-checks");
        await Assert.That(ciRules.Contains("node scripts/Features/RepositoryGovernance/verify.mjs",
            StringComparison.Ordinal)).IsTrue();
        var ciVerify = WorkflowLayoutSource.JobBlock(ci, "verify");
        await Assert.That(ciVerify.Contains("dotnet build KeyLoad.slnx --no-restore --configuration Release",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(ciVerify.Contains("dotnet format KeyLoad.slnx --verify-no-changes --no-restore",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(WorkflowLayoutSource.JobBlock(ci, "analyzer-rules")
            .Contains("tests/KeyLoad.Analyzers.Tests", StringComparison.Ordinal)).IsTrue();

        var tests = WorkflowLayoutSource.Read(TestsFile);
        await Assert.That(tests.StartsWith("name: Tests\n", StringComparison.Ordinal)).IsTrue();
        var testEvents = WorkflowLayoutSource.EventBlock(tests);
        await Assert.That(testEvents.Contains("pull_request:", StringComparison.Ordinal)).IsTrue();
        await Assert.That(testEvents.Contains("workflow_dispatch:", StringComparison.Ordinal)).IsTrue();
        await Assert.That(testEvents.Contains("branches: [main]", StringComparison.Ordinal)).IsTrue();
        var testJobs = WorkflowLayoutSource.JobIds(tests);
        await Assert.That(testJobs.Contains("repository-checks", StringComparer.Ordinal)).IsTrue();
        await Assert.That(testJobs.Contains("verify", StringComparer.Ordinal)).IsTrue();
        await Assert.That(testJobs.Contains("analyzer-rules", StringComparer.Ordinal)).IsTrue();
        await Assert.That(testJobs.Contains("docker-rf3", StringComparer.Ordinal)).IsTrue();
        await AssertOrdinaryQualification(WorkflowLayoutSource.JobBlock(tests, "verify"));
        await Assert.That(WorkflowLayoutSource.JobBlock(tests, "docker-rf3")
            .Contains("tests/KeyLoad.IntegrationTests", StringComparison.Ordinal)).IsTrue();
        await Assert.That(WorkflowLayoutSource.JobBlock(tests, "analyzer-rules")
            .Contains("tests/KeyLoad.Analyzers.Tests", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertOrdinaryQualification(string verify)
    {
        foreach (var command in new[]
        {
            "dotnet build KeyLoad.slnx --no-restore --configuration Release",
            "dotnet format KeyLoad.slnx --verify-no-changes --no-restore",
            "node scripts/Features/RepositoryGovernance/verify.mjs",
            "dotnet test --project tests/KeyLoad.Analyzers.Tests",
            "dotnet test --project tests/KeyLoad.UnitTests",
            "tests/KeyLoad.RecoveryTests",
            "DOTNET_EnableHWIntrinsic: 0",
        })
        {
            await Assert.That(verify.Contains(command, StringComparison.Ordinal)).IsTrue();
        }
    }

    private static bool IsComparisonJob(string job) => job.StartsWith("comparison-", StringComparison.Ordinal)
        || job.StartsWith("timeseries-", StringComparison.Ordinal)
        || job.StartsWith("benchmark-", StringComparison.Ordinal);
}
