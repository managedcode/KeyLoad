namespace KeyLoad.UnitTests.Features.RepositoryGovernance;

internal sealed class WorkflowLayoutCiSourceTests
{
    private const string CiFile = "ci.yml";
    private const string BenchmarksFile = "benchmarks.yml";
    private const string ReleaseFile = "release.yml";
    private const string OrdinaryEventGuard = "if: github.event_name != 'workflow_run'";
    private const string QualifySiteAction = "./control/.github/workflows/Features/BenchmarkComparisons/QualifySite";
    private const string DeploySiteAction = "./control/.github/workflows/Features/BenchmarkComparisons/DeploySite";

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
            new[] { "analyzer-rules", "deploy", "docker-rf3", "qualify", "repository-checks", "verify" })).IsTrue();
        await AssertRepositoryRules(ci);
        await AssertBuildAndTests(ci);
        await AssertRf3(ci);
        foreach (var jobId in new[] { "analyzer-rules", "docker-rf3", "repository-checks", "verify" })
        {
            await Assert.That(WorkflowLayoutSource.JobBlock(ci, jobId).Contains(OrdinaryEventGuard,
                StringComparison.Ordinal)).IsTrue();
        }
    }

    [Test]
    public async Task AcBcFail018To020CiBuildsLatestMetricsOnOwnMainOrCompletedBenchmarkEvents()
    {
        var ci = WorkflowLayoutSource.Read(CiFile);
        var events = WorkflowLayoutSource.EventBlock(ci);
        await Assert.That(events.Contains("workflow_run:\n    workflows: [Benchmarks]\n    types: [completed]",
            StringComparison.Ordinal)).IsTrue();
        var qualify = WorkflowLayoutSource.JobBlock(ci, "qualify");
        foreach (var required in new[]
        {
            "github.repository == 'managedcode/KeyLoad'", "((github.ref == 'refs/heads/main' &&",
            "(github.event_name == 'push' || github.event_name == 'workflow_dispatch')) ||",
            "(github.event_name == 'workflow_run' &&",
            "github.event.repository.id == 477801965", "github.event.workflow_run.head_repository.id == 477801965",
            "github.event.workflow_run.head_branch == 'main'", "github.event.workflow_run.name == 'Benchmarks'",
            "github.event.workflow_run.path == '.github/workflows/benchmarks.yml'",
            "github.event.workflow_run.status == 'completed'",
            "(github.event.workflow_run.event == 'push' || github.event.workflow_run.event == 'workflow_dispatch')",
            "(github.event.workflow_run.conclusion == 'success' || github.event.workflow_run.conclusion == 'failure')"
        })
        {
            await Assert.That(qualify.Contains(required, StringComparison.Ordinal)).IsTrue();
        }

        await Assert.That(qualify.Contains("needs:", StringComparison.Ordinal)).IsFalse();
        await Assert.That(qualify.Contains("github.event_name == 'pull_request'", StringComparison.Ordinal)).IsFalse();
        await Assert.That(qualify.Contains(QualifySiteAction, StringComparison.Ordinal)).IsTrue();
        await Assert.That(qualify.Contains("permissions: {contents: read, actions: read}", StringComparison.Ordinal)).IsTrue();
        await AssertTrustedControl(qualify);
    }

    [Test]
    public async Task AcBcFail018And021IndependentWebsiteQueueRetainsQualifiedLeastPrivilegePagesDelivery()
    {
        var ci = WorkflowLayoutSource.Read(CiFile);
        await Assert.That(ci.Contains("group: keyload-ci-${{ github.event_name == 'workflow_run' && 'site' || github.event_name == 'pull_request' && github.ref || github.run_id }}",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(ci.Contains("cancel-in-progress: ${{ github.event_name == 'pull_request' }}",
            StringComparison.Ordinal)).IsTrue();
        var deploy = WorkflowLayoutSource.JobBlock(ci, "deploy");
        var qualify = WorkflowLayoutSource.JobBlock(ci, "qualify");
        await Assert.That(qualify.Contains("concurrency:\n      group: keyload-site-qualification\n      cancel-in-progress: false",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(deploy.Contains("concurrency:\n      group: keyload-site-deployment\n      cancel-in-progress: false",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(deploy.Contains("needs: qualify", StringComparison.Ordinal)).IsTrue();
        await Assert.That(deploy.Contains("always() && !cancelled()", StringComparison.Ordinal)).IsTrue();
        await Assert.That(deploy.Contains("needs.qualify.result == 'success'", StringComparison.Ordinal)).IsTrue();
        await Assert.That(deploy.Contains("needs.qualify.outputs.mode == 'publish'", StringComparison.Ordinal)).IsTrue();
        await Assert.That(deploy.Contains("permissions: {contents: read, actions: read, pages: write, id-token: write}",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(deploy.Contains("environment: {name: github-pages", StringComparison.Ordinal)).IsTrue();
        await Assert.That(deploy.Contains(DeploySiteAction, StringComparison.Ordinal)).IsTrue();
        await Assert.That(deploy.Contains("QUALIFIED_REVISION: ${{ needs.qualify.outputs.site_revision }}",
            StringComparison.Ordinal)).IsTrue();
        await Assert.That(deploy.Contains("CONTROL_REVISION: ${{ github.workflow_sha }}", StringComparison.Ordinal)).IsTrue();
        await AssertTrustedControl(deploy);
    }

    private static async Task AssertTrustedControl(string job)
    {
        await Assert.That(job.Contains("ref: ${{ github.workflow_sha }}", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("path: control", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("persist-credentials: false", StringComparison.Ordinal)).IsTrue();
        await Assert.That(job.Contains("github.event.workflow_run.head_sha", StringComparison.Ordinal)).IsFalse();
        await Assert.That(job.Contains("continue-on-error", StringComparison.Ordinal)).IsFalse();
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
            "dotnet run --project src/KeyLoad.AppHost",
            "--KeyLoadTests:Suite=unit",
            "--KeyLoadTests:Suite=unit-scalar",
            "--KeyLoadTests:Suite=recovery",
            "node scripts/Features/RepositoryGovernance/verify.mjs",
        })
        {
            await Assert.That(ordinary.Contains(required, StringComparison.Ordinal)).IsTrue();
        }

        await Assert.That(WorkflowLayoutSource.JobBlock(ci, "analyzer-rules")
            .Contains("--KeyLoadTests:Suite=analyzers", StringComparison.Ordinal)).IsTrue();
    }

    private static async Task AssertRf3(string ci)
    {
        var rf3 = WorkflowLayoutSource.JobBlock(ci, "docker-rf3");
        await Assert.That(rf3.Contains("docker version", StringComparison.Ordinal)).IsTrue();
        await Assert.That(rf3.Contains("tests/KeyLoad.IntegrationTests", StringComparison.Ordinal)).IsTrue();
    }
}
