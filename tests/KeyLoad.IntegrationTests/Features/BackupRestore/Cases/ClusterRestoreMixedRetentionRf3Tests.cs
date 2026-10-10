using KeyLoad.IntegrationTests.Features.Messaging;

namespace KeyLoad.IntegrationTests.Features.BackupRestore;

[NotInParallel]
internal sealed class ClusterRestoreMixedRetentionRf3Tests
{
    [Test]
    [Arguments(QueueLifecyclePublicProtocol.Sdk)]
    [Arguments(QueueLifecyclePublicProtocol.Mcp)]
    [Arguments(QueueLifecyclePublicProtocol.Q1Sdk)]
    [Arguments(QueueLifecyclePublicProtocol.Q1Mcp)]
    public Task MixedRetainedStateRestoresPausedThenCurrentAuthorizationContinuesAndAllSixOwnersReopenCold(int route)
        => ClusterRestoreRf3Tests.RunAsync(async (source, seed, root, token) =>
        {
            var mixed = await ClusterRestoreMixedRetentionRf3Seed.RunAsync(source, seed, route, token).ConfigureAwait(false);
            await ClusterRestoreRf3Scenario.RunAsync(source, seed, root, null, mixed, token).ConfigureAwait(false);
        });
}
