using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class PartitionAuthorityResults
{
    internal static async Task HealthyAsync(KeyLoadClient sdk, McpOfficialClient session,
        PartitionAuthorityState state, long epoch, CancellationToken token)
    {
        var request = state.Scenario.Request();
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.PartitionQueryAsync(request, token));
        var official = await McpCallerAssertions.SuccessAsync<PartitionQueryPageV1>(await session.CallAsync(
            PartitionQueryRf3Protocol.ToolName, request, token));
        var call = SqlRf3Protocol.Call(state.Scenario.Partitions[PartitionAuthorityProtocol.FirstLeaf],
            PartitionQueryRf3Protocol.ToolName, request);
        await PageAsync(direct, state, epoch);
        await PageAsync(official.Value, state, epoch);
        await PageAsync(await SqlRf3Protocol.SdkAsync<PartitionQueryPageV1>(sdk, call, token), state, epoch);
        await PageAsync(await SqlRf3Protocol.McpAsync<PartitionQueryPageV1>(session, call, token), state, epoch);
    }

    private static async Task PageAsync(PartitionQueryPageV1 actual, PartitionAuthorityState state, long epoch)
    {
        await PartitionQueryRf3Assertions.AssertOracleAsync(actual, state.Scenario, state.Scenario.Partitions);
        await Assert.That(actual.Version).IsEqualTo(PartitionQueryRf3Protocol.Version);
        await Assert.That(actual.Complete).IsTrue();
        ImmutableArray<PartitionQueryRowV1> rows = [
            new(new(state.Scenario.Partitions[PartitionAuthorityProtocol.FirstLeaf], PartitionQueryRf3Protocol.Collection,
                PartitionAuthorityProtocol.FirstId), new(PartitionAuthorityProtocol.FirstId,
                PartitionQueryRf3Protocol.RowRevision, PartitionAuthorityProtocol.FirstProjection, false, [])),
            new(new(state.Scenario.Partitions[PartitionAuthorityProtocol.SecondLeaf], PartitionQueryRf3Protocol.Collection,
                PartitionAuthorityProtocol.SecondId), new(PartitionAuthorityProtocol.SecondId,
                PartitionQueryRf3Protocol.RowRevision, PartitionAuthorityProtocol.SecondProjection, false, []))];
        await SqlRf3Protocol.EqualAsync(rows, actual.Rows);
        await Assert.That(actual.Leaves.Length).IsEqualTo(PartitionAuthorityProtocol.LeafCount);
        for (var index = PartitionAuthorityProtocol.FirstLeaf; index < PartitionAuthorityProtocol.LeafCount; index++)
        {
            var leaf = actual.Leaves[index];
            await Assert.That(leaf.Partition).IsEqualTo(state.Scenario.Partitions[index]);
            await Assert.That(leaf.PolicyEpoch).IsEqualTo(epoch);
            await Assert.That(leaf.SchemaVersion).IsEqualTo(PartitionAuthorityProtocol.Schema);
            await Assert.That(leaf.CutPosition).IsGreaterThanOrEqualTo(0L);
            await Assert.That(leaf.AccessPath).IsEqualTo(PartitionAuthorityProtocol.FullScan);
        }
    }
}
