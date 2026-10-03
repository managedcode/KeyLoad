namespace KeyLoad.UnitTests.Features.RepositoryGovernance;

internal sealed class WorkflowLayoutCiSourceTests
{
    private const string CiFile = "ci.yml";
    private const string BenchmarksFile = "benchmarks.yml";
    private const string ReleaseFile = "release.yml";

    [Test]
    public async Task AcPipe001ExactlyThreeWorkflowsAndCiOwnsAllOrdinaryQualification()
    {
        var expectedFiles = new[] { CiFile, BenchmarksFile, ReleaseFile }.Order(StringComparer.Ordinal).ToArray();
        await Assert.That(WorkflowLayoutSource.TopLevelWorkflowFiles().SequenceEqual(expectedFiles)).IsTrue();

        var ci = WorkflowLayoutSource.Read(CiFile);
        await Assert.That(ci.StartsWith("name: CI\n", StringComparison.Ordinal)).IsTrue();
        var events = WorkflowLayoutSource.EventBlock(ci);
        await Assert.That(events.Contains("push:", StringComparison.Ordinal)).IsTrue();
        await Assert.That(events.Contains("pull_request:", StringComparison.Ordinal)).IsTrue();
        await Assert.That(events.Contains("workflow_dispatch:", StringComparison.Ordinal)).IsTrue();
        await Assert.That(events.Contains("branches: [main]", StringComparison.Ordinal)).IsTrue();

        var jobs = WorkflowLayoutSource.JobIds(ci);
        await Assert.That(jobs.Order(StringComparer.Ordinal).SequenceEqual(
            new[] { "analyzer-rules", "docker-rf3", "repository-checks", "verify" })).IsTrue();
        await AssertRepositoryRules(ci);
        await AssertBuildAndTests(ci);
        await AssertRf3(ci);
    }

    private static async Task AssertRepositoryRules(string ci)
    {
        var rules = WorkflowLayoutSource.JobBlock(ci, "repository-checks");
        await Assert.That(rules.Contains("node scripts/Features/RepositoryGovernance/verify.mjs",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(rules.Contains("needs:", StringComparison.Ordinal)).IsFalse();
    }

    private static async Task AssertBuildAndTests(string ci)
    {
        var ordinary = WorkflowLayoutSource.JobBlock(ci, "verify");
        foreach (var required in new[]
        {
            "dotnet build KeyLoad.slnx --no-restore --configuration Release",
            "dotnet format KeyLoad.slnx --verify-no-changes --no-restore",
            "dotnet test --project tests/KeyLoad.UnitTests",
            "tests/KeyLoad.RecoveryTests",
            "DOTNET_EnableHWIntrinsic: 0",
            "node scripts/Features/RepositoryGovernance/verify.mjs",
            "tests/KeyLoad.Analyzers.Tests",
        })
        {
            await Assert.That(ordinary.Contains(required, StringComparison.Ordinal)).IsTrue();
        }

        await Assert.That(WorkflowLayoutSource.JobBlock(ci, "analyzer-rules")
            .Contains("dotnet test --project tests/KeyLoad.Analyzers.Tests", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertRf3(string ci)
    {
        var rf3 = WorkflowLayoutSource.JobBlock(ci, "docker-rf3");
        await Assert.That(rf3.Contains("docker version", StringComparison.Ordinal)).IsTrue();
        await Assert.That(rf3.Contains("tests/KeyLoad.IntegrationTests", StringComparison.Ordinal)).IsTrue();
    }
}
