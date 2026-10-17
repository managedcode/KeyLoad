namespace KeyLoad.UnitTests.Features.ClusterRouting;

[NotInParallel]
internal sealed class RetiredOutcomeMetadataAuthorizationTests
{
    [Test]
    public Task ActualRetiredColdOwnerKeepsCorruptMetadataBehindCurrentPersistedAuthorization()
        => ControlledPartitionMovementTerminalOwners.ExecuteAsync(async (source, target, listeners, corpus) =>
        {
            await ControlledPartitionMovementTerminalTrial.ExecuteAsync(source, target, listeners, corpus);
            await RetiredOutcomeMetadataAuthorizationTrial.ExecuteAsync(source, target);
        });
}
