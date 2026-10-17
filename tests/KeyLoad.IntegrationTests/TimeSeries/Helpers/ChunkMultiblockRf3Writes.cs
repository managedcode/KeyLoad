using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMultiblockRf3Writes
{
    internal static async Task CommitAsync(RequestCqrsRf3Callers administrator, ChunkMultiblockRf3State state,
        Mutation mutation, string kind, long revision, bool official, ChunkMultiblockRf3Phase phase, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Scenario.Native.Partition, [mutation]);
        var failures = new List<Exception>();
        CommitReceipt? receipt = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            receipt = await ChunkMultiblockRf3Receipt.FirstAsync(administrator, command,
                new(kind, TimeSeriesRf3Scenario.Set, TimeSeriesRf3Scenario.Series, revision), official, token);
            state.Originals.Add((command, receipt));
        }, failures);
        state.Phase = phase;
        state.MaintenanceOutboxAdvanced = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            state.Outbox = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(command.Partition, token));
            await ChunkMultiblockRf3Current.RequireAsync(administrator, administrator, state, token);
            if (phase is ChunkMultiblockRf3Phase.OriginalPending or ChunkMultiblockRf3Phase.HealthyPending)
            {
                await WaitAsync(administrator, state, healthy: phase == ChunkMultiblockRf3Phase.HealthyPending, token);
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await ChunkMultiblockRf3Replay.RunAsync(administrator, state, command, receipt!, token);
    }

    internal static async Task WaitAsync(RequestCqrsRf3Callers administrator, ChunkMultiblockRf3State state,
        bool healthy, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var settled = healthy ? ChunkMultiblockRf3Phase.HealthyStable : ChunkMultiblockRf3Phase.OriginalStable;
        if (state.Phase == settled)
        {
            return;
        }
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadSampleChunkWindowAsync(
                ChunkMultiblockRf3Pages.Request(state), token));
            await ChunkMultiblockRf3Pages.WindowAsync(state, actual);
            var complete = actual.Generation == (healthy ? ChunkMultiblockRf3Expected.HealthyGeneration : ChunkMultiblockRf3Expected.OriginalGeneration);
            await ChunkMultiblockRf3Current.RequireAsync(administrator, administrator, state, token);
            if (complete)
            {
                state.Phase = healthy ? ChunkMultiblockRf3Phase.HealthyStable : ChunkMultiblockRf3Phase.OriginalStable;
                await ChunkMultiblockRf3Current.RequireAsync(administrator, administrator, state, token);
                return;
            }
            await Task.Delay(SampleChunkRf3Protocol.PollInterval, token);
        }
    }

    internal static async Task ReplayAsync(RequestCqrsRf3Callers administrator, ChunkMultiblockRf3State state, CancellationToken token)
    {
        foreach (var original in state.Originals)
        {
            await ChunkMultiblockRf3Replay.RunAsync(administrator, state, original.Command, original.Receipt, token);
        }
    }
}
