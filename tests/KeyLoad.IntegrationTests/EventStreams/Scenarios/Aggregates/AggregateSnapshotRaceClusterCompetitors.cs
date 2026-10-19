using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class AggregateSnapshotRaceClusterCompetitors
{
    internal static async Task RunAsync(RequestCqrsClusterCallers reader, AggregateSnapshotRaceClusterState state, CancellationToken token)
    {
        var sdk = EventAppendClusterCalls.SdkAsync(reader.Sdk, state.Commands[0], token);
        var mcp = EventAppendClusterCalls.McpAsync(reader.Mcp, state.Commands[1], token);
        EventAppendClusterOutcome? first = null;
        EventAppendClusterOutcome? second = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => first = await sdk.ConfigureAwait(false), failures);
        await ServerFailureObserver.ObserveAsync(async () => second = await mcp.ConfigureAwait(false), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That((first!.Receipt is not null) != (second!.Receipt is not null)).IsTrue();
        var sdkWon = first.Receipt is not null;
        state.Winner = state.Commands[sdkWon ? 0 : 1];
        state.Loser = state.Commands[sdkWon ? 1 : 0];
        state.WinnerReceipt = (sdkWon ? first : second).Receipt!;
        await AggregateSnapshotRaceClusterSeed.ReceiptAsync(state, state.WinnerReceipt, state.Winner.CommandId,
            new("storeAggregateSnapshot", AggregateReplayClusterTokens.StreamSet, AggregateReplayClusterTokens.StreamId, 1));
        await AggregateSnapshotRaceClusterFailures.OutcomeAsync(sdkWon ? second : first,
            ErrorCode.RevisionConflict, "The aggregate snapshot version has changed.", official: sdkWon);
    }
}
