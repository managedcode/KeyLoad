using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.Authorization;

internal static class PartitionAuthorityResults
{
    internal static async Task HealthyAsync(KeyLoadClient sdk, McpOfficialClient session,
        PartitionAuthorityState state, long epoch, CancellationToken token)
    {
        var request = state.Scenario.Request();
        var direct = await McpCallerAssertions.SdkSuccessAsync(await sdk.PartitionQueryAsync(request, token));
        var official = await McpCallerAssertions.SuccessAsync<PartitionQueryPage>(await session.CallAsync(
            PartitionQueryClusterProtocol.ToolName, request, token));
        var call = SqlClusterProtocol.Call(state.Scenario.Partitions[PartitionAuthorityProtocol.FirstLeaf],
            PartitionQueryClusterProtocol.ToolName, request);
        await PageAsync(direct, state, epoch);
        await PageAsync(official.Value, state, epoch);
        await PageAsync(await SqlClusterProtocol.SdkAsync<PartitionQueryPage>(sdk, call, token), state, epoch);
        await PageAsync(await SqlClusterProtocol.McpAsync<PartitionQueryPage>(session, call, token), state, epoch);
    }

    private static async Task PageAsync(PartitionQueryPage actual, PartitionAuthorityState state, long epoch)
    {
        await PartitionQueryClusterAssertions.AssertOracleAsync(actual, state.Scenario, state.Scenario.Partitions);
        await Assert.That(actual.Version).IsEqualTo(PartitionQueryClusterProtocol.Version);
        await Assert.That(actual.Complete).IsTrue();
        ImmutableArray<PartitionQueryRow> rows = [
            new(Reference(state, PartitionAuthorityProtocol.FirstLeaf, PartitionAuthorityProtocol.FirstId), new(PartitionAuthorityProtocol.FirstId,
                PartitionQueryClusterProtocol.RowRevision, PartitionAuthorityProtocol.FirstProjection, false, [])),
            new(Reference(state, PartitionAuthorityProtocol.SecondLeaf, PartitionAuthorityProtocol.SecondId), new(PartitionAuthorityProtocol.SecondId,
                PartitionQueryClusterProtocol.RowRevision, PartitionAuthorityProtocol.SecondProjection, false, []))];
        await SqlClusterProtocol.EqualAsync(rows, actual.Rows);
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

    private static EntityRef Reference(PartitionAuthorityState state, int index, string id)
        => state.Scenario.ExpectedReference(state.Scenario.Inputs.Single(input =>
            input.Partition == state.Scenario.Partitions[index] && input.Id == id));
}
