using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class AggregateSnapshotRaceRf3Competitors
{
    internal static async Task RunAsync(RequestCqrsRf3Callers reader, AggregateSnapshotRaceRf3State state, CancellationToken token)
    {
        var sdk = EventAppendRf3Calls.SdkAsync(reader.Sdk, state.Commands[0], token);
        var mcp = EventAppendRf3Calls.McpAsync(reader.Mcp, state.Commands[1], token);
        EventAppendRf3Outcome? first = null;
        EventAppendRf3Outcome? second = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => first = await sdk.ConfigureAwait(false), failures);
        await ServerFailureObserver.ObserveAsync(async () => second = await mcp.ConfigureAwait(false), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That((first!.Receipt is not null) != (second!.Receipt is not null)).IsTrue();
        var sdkWon = first.Receipt is not null;
        state.Winner = state.Commands[sdkWon ? 0 : 1];
        state.Loser = state.Commands[sdkWon ? 1 : 0];
        state.WinnerReceipt = (sdkWon ? first : second).Receipt!;
        await AggregateSnapshotRaceRf3Seed.ReceiptAsync(state, state.WinnerReceipt, state.Winner.CommandId,
            new("storeAggregateSnapshot", AggregateReplayRf3Tokens.StreamSet, AggregateReplayRf3Tokens.StreamId, 1));
        await AggregateSnapshotRaceRf3Failures.OutcomeAsync(sdkWon ? second : first,
            ErrorCode.RevisionConflict, "The aggregate snapshot version has changed.", official: sdkWon);
    }
}
