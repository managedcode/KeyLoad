namespace KeyLoad.FunctionalTests.TimeSeries;

internal sealed class SampleChunkCanonicalRollbackTests
{
    [Test]
    public async Task AcChunk008012FailedWholeAtomicAppendAndSealRetainsOriginalOutcomeThenHealthy()
        => await SampleChunkAtomicRollbackFlow.RunAsync();

    [Test]
    public async Task AcChunk007EnrollmentRejectsExistingRawRangeAndRetainsAllRowsBeforeHealthyEmptyWindow()
        => await SampleChunkEnrollmentBoundaryFlow.RunAsync();
}
