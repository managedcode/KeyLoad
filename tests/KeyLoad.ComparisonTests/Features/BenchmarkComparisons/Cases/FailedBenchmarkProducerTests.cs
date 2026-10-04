namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-BC-FAIL-003: real Node and files validate complete failed cohorts without authenticating fixtures.</summary>
internal sealed class FailedBenchmarkProducerTests
{
    [Test]
    public async Task CompleteFailedCohortPreservesFailureProofAndRejectsMismatches()
    {
        var result = await FailedBenchmarkNodeProcess.RunAsync(FailedBenchmarkProducerProgram.Source,
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Error).IsEmpty();
        await Assert.That(result.Output).IsEqualTo("accepted\n");
    }

    [Test]
    public async Task FinalizationRetainsPriorBytesAndRejectsInvalidOrSymlinkedEvidence()
    {
        var result = await FailedBenchmarkNodeProcess.RunAsync(FailedBenchmarkFinalizeProgram.Source,
            TestContext.Current!.Execution.CancellationToken);
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.Error).IsEmpty();
        await Assert.That(result.Output).IsEqualTo("accepted\n");
    }

}
