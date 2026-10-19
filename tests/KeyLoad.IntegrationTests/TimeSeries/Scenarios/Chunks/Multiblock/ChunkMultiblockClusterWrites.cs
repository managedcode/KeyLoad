using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.TimeSeries;

internal static class ChunkMultiblockClusterWrites
{
    internal static async Task CommitAsync(RequestCqrsClusterCallers administrator, ChunkMultiblockClusterState state,
        Mutation mutation, string kind, long revision, bool official, ChunkMultiblockClusterPhase phase, CancellationToken token)
    {
        var command = new CommandRequest(Guid.NewGuid(), state.Scenario.Execution.Partition, [mutation]);
        var failures = new List<Exception>();
        CommitReceipt? receipt = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            receipt = await ChunkMultiblockClusterReceipt.FirstAsync(administrator, command,
                new(kind, TimeSeriesClusterScenario.Set, TimeSeriesClusterScenario.Series, revision), official, token);
            state.Originals.Add((command, receipt));
        }, failures);
        state.Phase = phase;
        state.MaintenanceOutboxAdvanced = false;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            state.Outbox = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.OutboxStatusAsync(command.Partition, token));
            await ChunkMultiblockClusterCurrent.RequireAsync(administrator, administrator, state, token);
            if (phase is ChunkMultiblockClusterPhase.OriginalPending or ChunkMultiblockClusterPhase.HealthyPending)
            {
                await WaitAsync(administrator, state, healthy: phase == ChunkMultiblockClusterPhase.HealthyPending, token);
            }
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await ChunkMultiblockClusterReplay.RunAsync(administrator, state, command, receipt!, token);
    }

    internal static async Task WaitAsync(RequestCqrsClusterCallers administrator, ChunkMultiblockClusterState state,
        bool healthy, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var settled = healthy ? ChunkMultiblockClusterPhase.HealthyStable : ChunkMultiblockClusterPhase.OriginalStable;
        if (state.Phase == settled)
        {
            return;
        }
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var actual = await McpCallerAssertions.SdkSuccessAsync(await administrator.Sdk.ReadSampleChunkWindowAsync(
                ChunkMultiblockClusterPages.Request(state), token));
            await ChunkMultiblockClusterPages.WindowAsync(state, actual);
            var complete = actual.Generation == (healthy ? ChunkMultiblockClusterExpected.HealthyGeneration : ChunkMultiblockClusterExpected.OriginalGeneration);
            await ChunkMultiblockClusterCurrent.RequireAsync(administrator, administrator, state, token);
            if (complete)
            {
                state.Phase = healthy ? ChunkMultiblockClusterPhase.HealthyStable : ChunkMultiblockClusterPhase.OriginalStable;
                await ChunkMultiblockClusterCurrent.RequireAsync(administrator, administrator, state, token);
                return;
            }
            await Task.Delay(SampleChunkClusterProtocol.PollInterval, token);
        }
    }

    internal static async Task ReplayAsync(RequestCqrsClusterCallers administrator, ChunkMultiblockClusterState state, CancellationToken token)
    {
        foreach (var original in state.Originals)
        {
            await ChunkMultiblockClusterReplay.RunAsync(administrator, state, original.Command, original.Receipt, token);
        }
    }
}
