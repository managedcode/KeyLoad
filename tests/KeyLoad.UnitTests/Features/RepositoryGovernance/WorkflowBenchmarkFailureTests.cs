namespace KeyLoad.UnitTests.Features.RepositoryGovernance;

/// <summary>AC-BC-FAIL-001/002: failed workloads retain status and cannot skip result publication.</summary>
internal sealed class WorkflowBenchmarkFailureTests
{
    private const string WorkloadName = "name: Run database workload";
    private const string AvailabilityName = "name: Record benchmark availability";
    private const string UploadName = "name: Save benchmark results";
    private const string Always = "if: always()";

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
            await Assert.That(workload.Contains(Always, StringComparison.Ordinal)).IsTrue();
            await Assert.That(workload.Contains("exit 1", StringComparison.Ordinal)).IsTrue();
            await Assert.That(finalize.Contains(Always, StringComparison.Ordinal)).IsTrue();
            await Assert.That(finalize.Contains("${{ steps.workload.outcome }}", StringComparison.Ordinal)).IsTrue();
            await Assert.That(finalize.Contains("finalize-worker.mjs", StringComparison.Ordinal)).IsTrue();
            await Assert.That(upload.Contains(Always, StringComparison.Ordinal)).IsTrue();
            await Assert.That(job.IndexOf(WorkloadName, StringComparison.Ordinal))
                .IsLessThan(job.IndexOf(AvailabilityName, StringComparison.Ordinal));
            await Assert.That(job.IndexOf(AvailabilityName, StringComparison.Ordinal))
                .IsLessThan(job.IndexOf(UploadName, StringComparison.Ordinal));
            await Assert.That(job.Contains("continue-on-error", StringComparison.Ordinal)).IsFalse();
        }
    }

    [Test]
    public async Task AggregateWaitsForEveryMatrixAndPublishesDespiteUnrelatedBuildOrWorkloadFailure()
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
            var job = WorkflowLayoutSource.JobBlock(workflow, jobId);
            await Assert.That(job.Contains("always() && !cancelled()", StringComparison.Ordinal)).IsTrue();
        }
    }
}
