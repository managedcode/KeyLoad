namespace KeyLoad.UnitTests.Features.BackupRestore;

internal sealed class FreshArchiveConservationTests
{
    [Test]
    [Arguments(false, false)]
    [Arguments(false, true)]
    [Arguments(true, false)]
    [Arguments(true, true)]
    public Task OriginalArchiveRejectsMalformedOrMissingConservationFloorWithoutPublishingAndSourceColdBytesRemainExact(
        bool malformedFloor, bool existingEmpty)
        => FreshArchiveConservationFlow.RunAsync(malformedFloor, existingEmpty,
            TestContext.Current!.Execution.CancellationToken);
}
