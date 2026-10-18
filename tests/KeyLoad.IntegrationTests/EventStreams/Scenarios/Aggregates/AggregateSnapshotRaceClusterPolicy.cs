using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class AggregateSnapshotRaceClusterPolicy
{
    internal static async Task RepairAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        AggregateSnapshotRaceClusterState state, CancellationToken token)
    {
        await ConfigureAsync(root, state, 2, false, token);
        await AggregateSnapshotRaceClusterCurrent.ObserveAsync(root, state, async () =>
        {
            await AggregateSnapshotRaceClusterFailures.ReadDeniedAsync(reader, state, token);
            await AggregateSnapshotRaceClusterFailures.CommandsAsync(reader, state, true, token);
        }, token);
        await ConfigureAsync(root, state, 3, true, token);
        await AggregateSnapshotRaceClusterCurrent.ObserveAsync(root, state, async () =>
        {
            await AggregateSnapshotRaceClusterFailures.CommandsAsync(reader, state, true, token);
            await AggregateSnapshotRaceClusterCurrent.RequireAsync(reader, state, token);
        }, token);
        state.Healthy = AggregateSnapshotRaceClusterState.Snapshot(state.Scenario, 3, "{\"count\":6}", 1);
        state.HealthyReceipt = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.CommitAsync(state.Healthy, token));
        await AggregateSnapshotRaceClusterSeed.ReceiptAsync(state, state.HealthyReceipt, state.Healthy.CommandId,
            new("storeAggregateSnapshot", AggregateReplayClusterTokens.StreamSet, AggregateReplayClusterTokens.StreamId, 2));
        state.Updated = true;
        state.OriginalImage = await AggregateSnapshotRaceClusterCurrent.ImageAsync(root, state, token);
        await WaveAsync(root, reader, state, token);
    }
    internal static Task WaveAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        AggregateSnapshotRaceClusterState state, CancellationToken token)
        => AggregateSnapshotRaceClusterCurrent.ObserveAsync(root, state, async () =>
        {
            await SqlClusterProtocol.EqualAsync(state.OriginalImage, await AggregateSnapshotRaceClusterCurrent.ImageAsync(root, state, token));
            await AggregateSnapshotRaceClusterCurrent.RequireAsync(reader, state, token);
            await AggregateSnapshotRaceClusterFailures.ReplayAsync(root, state.SeedCommand, state.Seed, token);
            await AggregateSnapshotRaceClusterFailures.CommandsAsync(reader, state, true, token);
            await AggregateSnapshotRaceClusterFailures.ReplayAsync(reader, state.Healthy!, state.HealthyReceipt!, token);
        }, token);
    private static async Task ConfigureAsync(RequestCqrsClusterCallers root, AggregateSnapshotRaceClusterState state,
        long epoch, bool allowed, CancellationToken token)
    {
        var principal = state.Scenario.Worker with
        {
            PolicyEpoch = epoch,
            FieldGrants = allowed ? state.Scenario.Worker.FieldGrants
                : [.. state.Scenario.Worker.FieldGrants.Where(grant => grant != AggregateReplayClusterTokens.PayloadUseGrant)]
        };
        await SqlClusterProtocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));
    }
}
