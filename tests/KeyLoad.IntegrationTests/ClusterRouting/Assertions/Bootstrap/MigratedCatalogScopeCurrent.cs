using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class MigratedCatalogScopeCurrent
{
    private const int PlacementVersion = 1;
    private const long EmptyTail = 0;
    private const long FirstAvailable = 1;
    private const long EmptyStoredRecords = 0;
    private const long EmptyStoredBytes = 0;
    internal static async Task RunAsync(PartitionMovementPublicParentRf3Seed seed,
        TransferredAtomicScopeState atomic, MigratedAtomicDomainState domain,
        ImmutableArray<MigratedCatalogScopeState> scopes, CancellationToken token)
    {
        await MigratedAtomicDomainCurrent.RunAsync(seed, atomic, domain, token);
        var expected = new OutboxStatus(new(EmptyTail, FirstAvailable, EmptyStoredRecords, EmptyStoredBytes), []);
        foreach (var scope in scopes)
        {
            var request = new AtomicPartitionPlacementReadRequest(PlacementVersion, scope.Refused.Partition);
            foreach (var sdk in new[] { seed.Source, seed.Target })
            {
                await SqlRf3Protocol.EqualAsync(scope.Fallback,
                    await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadAtomicPartitionPlacementAsync(request, token)));
                await SqlRf3Protocol.EqualAsync(expected,
                    await McpCallerAssertions.SdkSuccessAsync(await sdk.OutboxStatusAsync(scope.Refused.Partition, token)));
            }
            await SqlRf3Protocol.EqualAsync(scope.Fallback,
                (await McpCallerAssertions.SuccessAsync<AtomicPartitionPlacementResolution>(await seed.Official.CallAsync(
                    AtomicPartitionPlacementPublicRf3Protocol.ReadTool, request, token))).Value);
            await SqlRf3Protocol.EqualAsync(expected,
                (await McpCallerAssertions.SuccessAsync<OutboxStatus>(await seed.Official.CallAsync(
                    McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(scope.Refused.Partition), token))).Value);
        }
    }
}
