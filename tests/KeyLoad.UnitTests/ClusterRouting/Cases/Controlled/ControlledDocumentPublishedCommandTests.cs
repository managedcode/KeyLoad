namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class ControlledDocumentPublishedCommandTests
{
    [Test]
    public async Task ActualRetiredOwnersApplyAndRetainTheOriginalDocumentReceiptAcrossColdReopen()
        => await ControlledPartitionMovementTerminalOwners.ExecuteAsync(async (source, target, listeners, corpus) =>
        {
            await ControlledPartitionMovementTerminalTrial.ExecuteAsync(source, target, listeners, corpus);
            await ControlledDocumentNativeCommandOwners.ExecuteAsync(source, target, listeners, corpus);
        });
}
