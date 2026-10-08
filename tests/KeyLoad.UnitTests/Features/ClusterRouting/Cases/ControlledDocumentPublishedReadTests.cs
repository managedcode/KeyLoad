namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class ControlledDocumentPublishedReadTests
{
    [Test]
    public Task ActualRetiredOwnersDenyUnknownSourceThenReadLiteralTargetWithoutChangingEitherStore()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(async (source, target, listeners, corpus) =>
        {
            await ControlledPartitionMovementTerminalTrial.ExecuteAsync(source, target, listeners, corpus);
            await ControlledDocumentPublishedReadFlow.ExecuteAsync(source, target);
        });
}
