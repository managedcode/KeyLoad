using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

internal static class ControlledDocumentLifetimeRf3Flow
{
    private const int ColdCount = 2;

    internal static async Task RunAsync(TwoRf3MembershipWave wave, CancellationToken token)
    {
        PartitionMovementPublicParentRf3Seed? seed = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            seed = await PartitionMovementPublicParentRf3Seed.CreateAsync(wave, token);
            var result = await McpCallerAssertions.SdkSuccessAsync(await seed.Source.MovePartitionAsync(seed.FirstRequest, token));
            await Assert.That(result.Phase).IsEqualTo(PartitionMovePhase.Retired);
            await seed.VerifyAsync(token);
            await SqlRf3Protocol.EqualAsync(result, (await McpCallerAssertions.SuccessAsync<PartitionMoveResult>(
                await seed.Official.CallAsync(PartitionMovePublicProtocol.ToolName, seed.FirstRequest, token))).Value);
            for (var cold = default(int); cold < ColdCount; cold++)
            {
                _ = await wave.StopForDirectoryReadAsync();
                await wave.RestartJoinedAsync(token);
                await seed.ReconnectAsync(wave, token);
                await SqlRf3Protocol.EqualAsync(result,
                    await McpCallerAssertions.SdkSuccessAsync(await seed.Source.MovePartitionAsync(seed.FirstRequest, token)));
                await seed.VerifyAsync(token);
            }
        }, failures);
        if (seed is { } owned)
        {
            await ServerFailureObserver.ObserveAsync(() => owned.DisposeAsync().AsTask(), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
