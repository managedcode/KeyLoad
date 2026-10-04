using KeyLoad.UnitTests.Features.RepositoryGovernance;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class NativeSerializationWorkflowTests
{
    private const string Workflow = "benchmarks.yml";
    private const string AggregateNeeds = "needs: [comparison-build, comparison-plan, comparison-images, comparison-preflight, comparison-crud, comparison-specialized]";
    private static readonly string[] CompletePipelineJobs =
    [
        "comparison-build",
        "comparison-plan",
        "comparison-images",
        "comparison-preflight",
        "comparison-crud",
        "comparison-specialized",
        "comparison-aggregate"
    ];
    private static readonly string[] RemovedDiagnosticJobs = ["native-serialization", "raw-storage", "internal-codec"];

    [Test]
    public async Task AcPerf004WorkflowRunsTheCompleteDatabaseComparisonPipelineWithoutDiagnosticModes()
    {
        var workflow = WorkflowLayoutSource.Read(Workflow);
        var events = WorkflowLayoutSource.EventBlock(workflow);
        var jobIds = WorkflowLayoutSource.JobIds(workflow);

        await Assert.That(events.Contains("workflow_dispatch:", StringComparison.Ordinal)).IsTrue();
        foreach (var removedMode in new[] { "native_serialization_only", "raw_storage_only" })
        {
            await Assert.That(events.Contains(removedMode, StringComparison.Ordinal)).IsFalse();
            await Assert.That(workflow.Contains(removedMode, StringComparison.Ordinal)).IsFalse();
        }

        foreach (var job in CompletePipelineJobs)
        {
            await Assert.That(jobIds.Contains(job, StringComparer.Ordinal)).IsTrue();
        }

        foreach (var job in RemovedDiagnosticJobs)
        {
            await Assert.That(jobIds.Contains(job, StringComparer.Ordinal)).IsFalse();
        }

        var aggregate = WorkflowLayoutSource.JobBlock(workflow, "comparison-aggregate");
        await Assert.That(aggregate.Contains(AggregateNeeds, StringComparison.Ordinal)).IsTrue();
        await Assert.That(aggregate.Contains("Check all 270 benchmark results", StringComparison.Ordinal)).IsTrue();
        await Assert.That(aggregate.Contains("raw-storage", StringComparison.Ordinal)).IsFalse();
        await Assert.That(aggregate.Contains("internal-codec", StringComparison.Ordinal)).IsFalse();

        await Assert.That(jobIds.Contains("qualify", StringComparer.Ordinal)).IsFalse();
        await Assert.That(jobIds.Contains("deploy", StringComparer.Ordinal)).IsFalse();
        var ci = WorkflowLayoutSource.Read("ci.yml");
        await Assert.That(WorkflowLayoutSource.JobBlock(ci, "qualify").Contains("needs:", StringComparison.Ordinal)).IsFalse();
        await Assert.That(WorkflowLayoutSource.JobBlock(ci, "deploy")
            .Contains("needs: qualify", StringComparison.Ordinal)).IsTrue();
    }
}
