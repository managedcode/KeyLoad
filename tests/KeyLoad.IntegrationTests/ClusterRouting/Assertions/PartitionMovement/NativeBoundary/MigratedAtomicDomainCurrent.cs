using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MigratedAtomicDomainCurrent
{
    internal static async Task RunAsync(PartitionMovementPublicParentRf3Seed seed,
        TransferredAtomicScopeState atomic, MigratedAtomicDomainState domain, CancellationToken token)
    {
        await TransferredAtomicScopeCurrent.RequireAsync(seed, atomic, token);
        var request = new AtomicPartitionPlacementReadRequest(1, domain.Refused.Partition);
        var expected = new OutboxStatus(new(0, 1, 0, 0), []);
        foreach (var sdk in new[] { seed.Source, seed.Target })
        {
            await SqlRf3Protocol.EqualAsync(domain.Fallback,
                await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadAtomicPartitionPlacementAsync(request, token)));
            await SqlRf3Protocol.EqualAsync(expected,
                await McpCallerAssertions.SdkSuccessAsync(await sdk.OutboxStatusAsync(domain.Refused.Partition, token)));
        }
        await SqlRf3Protocol.EqualAsync(domain.Fallback,
            (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(await seed.Official.CallAsync(
                AtomicPartitionPlacementPublicRf3Protocol.ReadTool, request, token))).Value);
        await SqlRf3Protocol.EqualAsync(expected,
            (await McpCallerAssertions.SuccessAsync<OutboxStatus>(await seed.Official.CallAsync(
                McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(domain.Refused.Partition), token))).Value);
    }
}
