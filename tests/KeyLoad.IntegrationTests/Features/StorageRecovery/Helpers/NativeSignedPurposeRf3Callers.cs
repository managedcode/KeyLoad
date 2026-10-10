using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NativeSignedPurposeRf3Callers
{
    internal static async Task RequireRefusedAsync(PartitionMovementPublicParentRf3Seed seed,
        CommandRequest command, bool official, CancellationToken token)
    {
        if (official)
        {
            await McpCallerAssertions.ErrorAsync(await seed.Official.CallAsync(
                McpCallerTools.DocumentsCommit, command, token).ConfigureAwait(false), ErrorCode.TokenInvalidated, dispatched: true);
            return;
        }
        var result = await seed.Source.CommitAsync(command, token).ConfigureAwait(false);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
    }
}
