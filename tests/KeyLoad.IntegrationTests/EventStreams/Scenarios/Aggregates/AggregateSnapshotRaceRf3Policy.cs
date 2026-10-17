using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class AggregateSnapshotRaceRf3Policy
{
    internal static async Task RepairAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        AggregateSnapshotRaceRf3State state, CancellationToken token)
    {
        await ConfigureAsync(root, state, 2, false, token);
        await AggregateSnapshotRaceRf3Current.ObserveAsync(root, state, async () =>
        {
            await AggregateSnapshotRaceRf3Failures.ReadDeniedAsync(reader, state, token);
            await AggregateSnapshotRaceRf3Failures.CommandsAsync(reader, state, true, token);
        }, token);
        await ConfigureAsync(root, state, 3, true, token);
        await AggregateSnapshotRaceRf3Current.ObserveAsync(root, state, async () =>
        {
            await AggregateSnapshotRaceRf3Failures.CommandsAsync(reader, state, true, token);
            await AggregateSnapshotRaceRf3Current.RequireAsync(reader, state, token);
        }, token);
        state.Healthy = AggregateSnapshotRaceRf3State.Snapshot(state.Scenario, 3, "{\"count\":6}", 1);
        state.HealthyReceipt = await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.CommitAsync(state.Healthy, token));
        await AggregateSnapshotRaceRf3Seed.ReceiptAsync(state, state.HealthyReceipt, state.Healthy.CommandId,
            new("storeAggregateSnapshot", AggregateReplayRf3Tokens.StreamSet, AggregateReplayRf3Tokens.StreamId, 2));
        state.Updated = true;
        state.OriginalImage = await AggregateSnapshotRaceRf3Current.ImageAsync(root, state, token);
        await WaveAsync(root, reader, state, token);
    }
    internal static Task WaveAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        AggregateSnapshotRaceRf3State state, CancellationToken token)
        => AggregateSnapshotRaceRf3Current.ObserveAsync(root, state, async () =>
        {
            await SqlRf3Protocol.EqualAsync(state.OriginalImage, await AggregateSnapshotRaceRf3Current.ImageAsync(root, state, token));
            await AggregateSnapshotRaceRf3Current.RequireAsync(reader, state, token);
            await AggregateSnapshotRaceRf3Failures.ReplayAsync(root, state.SeedCommand, state.Seed, token);
            await AggregateSnapshotRaceRf3Failures.CommandsAsync(reader, state, true, token);
            await AggregateSnapshotRaceRf3Failures.ReplayAsync(reader, state.Healthy!, state.HealthyReceipt!, token);
        }, token);
    private static async Task ConfigureAsync(RequestCqrsRf3Callers root, AggregateSnapshotRaceRf3State state,
        long epoch, bool allowed, CancellationToken token)
    {
        var principal = state.Scenario.Worker with
        {
            PolicyEpoch = epoch,
            FieldGrants = allowed ? state.Scenario.Worker.FieldGrants
                : [.. state.Scenario.Worker.FieldGrants.Where(grant => grant != AggregateReplayRf3Tokens.PayloadUseGrant)]
        };
        await SqlRf3Protocol.EqualAsync(principal, await McpCallerAssertions.SdkSuccessAsync(
            await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), principal, token)));
    }
}
