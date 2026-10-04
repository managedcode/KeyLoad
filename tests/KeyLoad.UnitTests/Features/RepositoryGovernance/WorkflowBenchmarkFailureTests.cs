namespace KeyLoad.UnitTests.Features.RepositoryGovernance;

/// <summary>AC-BC-FAIL-001/002/008/017: failures retain database JSON while cancellation stops owned work.</summary>
internal sealed class WorkflowBenchmarkFailureTests
{
    private const string WorkloadName = "name: Run database workload";
    private const string AvailabilityName = "name: Record benchmark availability";
    private const string UploadName = "name: Save benchmark results";
    private const string Always = "if: always()";
    private const string NotCancelled = "if: ${{ !cancelled() }}";
    private const string CleanupName = "name: Clean up containers and save logs";
    private const string TeardownPath = "Features/BenchmarkComparisons/IsolatedCellTeardown/action.yml";

    [Test]
    public async Task EveryCellFinalizesAvailabilityAfterActualWorkloadOutcomeBeforeUpload()
    {
        var workflow = WorkflowLayoutSource.Read("benchmarks.yml");
        foreach (var jobId in new[] { "comparison-preflight", "comparison-crud", "comparison-specialized" })
        {
            var job = WorkflowLayoutSource.JobBlock(workflow, jobId);
            var steps = WorkflowStepNameTests.StepBlocks(job);
            var workload = steps.Single(step => step.Contains(WorkloadName, StringComparison.Ordinal));
            var finalize = steps.Single(step => step.Contains(AvailabilityName, StringComparison.Ordinal));
            var upload = steps.Single(step => step.Contains(UploadName, StringComparison.Ordinal));
            await Assert.That(workload.Contains(NotCancelled, StringComparison.Ordinal)).IsTrue();
            await Assert.That(workload.Contains("exit 1", StringComparison.Ordinal)).IsTrue();
            await Assert.That(finalize.Contains(NotCancelled, StringComparison.Ordinal)).IsTrue();
            await Assert.That(finalize.Contains("${{ steps.workload.outcome }}", StringComparison.Ordinal)).IsTrue();
            await Assert.That(finalize.Contains("finalize-worker.mjs", StringComparison.Ordinal)).IsTrue();
            await Assert.That(upload.Contains(NotCancelled, StringComparison.Ordinal)).IsTrue();
            await Assert.That(job.IndexOf(WorkloadName, StringComparison.Ordinal))
                .IsLessThan(job.IndexOf(AvailabilityName, StringComparison.Ordinal));
            await Assert.That(job.IndexOf(AvailabilityName, StringComparison.Ordinal))
                .IsLessThan(job.IndexOf(UploadName, StringComparison.Ordinal));
            var cleanup = steps.Single(step => step.Contains(CleanupName, StringComparison.Ordinal));
            await Assert.That(cleanup.Contains(Always, StringComparison.Ordinal)).IsTrue();
            await Assert.That(job.Contains("continue-on-error", StringComparison.Ordinal)).IsFalse();
        }
    }

    [Test]
    public async Task CancelledImagePreparationStillCleansOwnedRegistryAndRetainsDiagnostics()
    {
        var workflow = WorkflowLayoutSource.Read("benchmarks.yml");
        var images = WorkflowLayoutSource.JobBlock(workflow, "comparison-images");
        var cleanup = WorkflowStepNameTests.StepBlocks(images).Single(step =>
            step.Contains("name: Clean up Docker registry after image failure", StringComparison.Ordinal));
        await Assert.That(cleanup.Contains("if: ${{ always() &&", StringComparison.Ordinal)).IsTrue();
        await Assert.That(cleanup.Contains("steps.images.outcome == 'cancelled'", StringComparison.Ordinal)).IsTrue();
        var teardown = WorkflowLayoutSource.Read(TeardownPath);
        var steps = WorkflowStepNameTests.StepBlocks(teardown);
        var registry = steps.Single(step => step.Contains("name: Clean up this job's Docker registry", StringComparison.Ordinal));
        await Assert.That(registry.Contains("if: ${{ always() &&", StringComparison.Ordinal)).IsTrue();
        await Assert.That(registry.Contains("inputs.setup-outcome == 'cancelled'", StringComparison.Ordinal)).IsTrue();
        await Assert.That(registry.Contains("cleanup-images.mjs", StringComparison.Ordinal)).IsTrue();
        var diagnostics = steps.Single(step => step.Contains("name: Save container logs and test results", StringComparison.Ordinal));
        await Assert.That(diagnostics.Contains(Always, StringComparison.Ordinal)).IsTrue();
    }

    [Test]
    public async Task AggregateWaitsForEveryMatrixAndSavesJsonDespiteUnrelatedBuildOrWorkloadFailure()
    {
        var workflow = WorkflowLayoutSource.Read("benchmarks.yml");
        var aggregate = WorkflowLayoutSource.JobBlock(workflow, "comparison-aggregate");
        await Assert.That(aggregate.Contains("always() && !cancelled()", StringComparison.Ordinal)).IsTrue();
        await Assert.That(aggregate.Contains("needs.comparison-images.result == 'success'", StringComparison.Ordinal)).IsTrue();
        await Assert.That(aggregate.Contains("needs.comparison-plan.result == 'success'", StringComparison.Ordinal)).IsTrue();
        await Assert.That(aggregate.Contains("needs.comparison-build.result == 'success'", StringComparison.Ordinal)).IsFalse();
        await Assert.That(aggregate.Contains("needs.comparison-crud.result == 'success'", StringComparison.Ordinal)).IsFalse();
        await Assert.That(aggregate.Contains("comparison-preflight, comparison-crud, comparison-specialized", StringComparison.Ordinal)).IsTrue();
        foreach (var jobId in new[] { "qualify", "deploy" })
        {
            await Assert.That(WorkflowLayoutSource.JobBlock(workflow, jobId)).IsEqualTo(string.Empty);
        }

        await Assert.That(aggregate.Contains("name: comparison-isolated-suite", StringComparison.Ordinal)).IsTrue();
        await Assert.That(aggregate.Contains("name: comparison-isolated-provider-evidence", StringComparison.Ordinal)).IsTrue();
        await Assert.That(workflow.Contains("site/scripts/build.mjs", StringComparison.Ordinal)).IsFalse();
        await Assert.That(workflow.Contains("comparison-isolated-site-candidate", StringComparison.Ordinal)).IsFalse();
    }
}
