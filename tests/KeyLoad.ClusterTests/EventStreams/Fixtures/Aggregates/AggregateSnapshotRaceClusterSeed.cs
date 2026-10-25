using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;

namespace KeyLoad.ClusterTests.EventStreams;

internal static class AggregateSnapshotRaceClusterSeed
{
    internal static async Task<AggregateSnapshotRaceClusterState> RunAsync(ClusterFixture fixture,
        RequestCqrsClusterCallers root, CancellationToken token)
    {
        var scenario = await AggregateReplayClusterScenario.CreateAsync(fixture, token);
        var id = Guid.NewGuid();
        var seed = await scenario.AppendInitialAsync(root.Sdk, id, token);
        var state = new AggregateSnapshotRaceClusterState(scenario, seed)
        {
            SeedCommand = new(id, scenario.Partition, [new AppendEvents(AggregateReplayClusterTokens.StreamSet,
                AggregateReplayClusterTokens.StreamId, [Data(0), Data(1), Data(2)], ExpectedStreamRevision.NoStream)])
        };
        await ReceiptAsync(state, seed, id, new("appendEvents", AggregateReplayClusterTokens.StreamSet,
            AggregateReplayClusterTokens.StreamId, 3));
        var full = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAggregateReplayAsync(
            scenario.ReadRequest(3) with
            {
                FromBeginning = true
            }, token));
        await Assert.That(full.Stream).IsEqualTo(scenario.Stream);
        await Assert.That(full.Head).IsEqualTo(new StreamHead(3, 1, 1));
        await Assert.That(full.Snapshot).IsNull();
        await Assert.That(full.CutPosition).IsGreaterThan(0L);
        await Assert.That(full.Events.Length).IsEqualTo(3);
        for (var index = 0; index < full.Events.Length; index++)
        {
            var record = full.Events[index];
            await Assert.That(record.Stream).IsEqualTo(scenario.Stream);
            await Assert.That(record.Revision).IsEqualTo(index + 1L);
            await Assert.That(record.EventSequence).IsEqualTo(index + 1L);
            await Assert.That(record.RecordedAt).IsGreaterThan(DateTimeOffset.UnixEpoch);
            await Assert.That(record.RecordedAt).IsEqualTo(full.Events[0].RecordedAt);
            await SqlClusterProtocol.EqualAsync(Data(index), record.Data);
        }
        state.Events = full.Events;
        state.OriginalImage = await AggregateSnapshotRaceClusterCurrent.ImageAsync(root, state, token);
        return state;
    }
    internal static async Task ReceiptAsync(AggregateSnapshotRaceClusterState state, CommitReceipt actual,
        Guid id, MutationReceipt mutation)
    {
        await SqlClusterProtocol.EqualAsync(new CommitReceipt(id, new(actual.Token.Incarnation,
            state.Scenario.Partition.AtomicPartitionId, actual.Token.Position),
            [mutation], DurabilityProfile.ProcessDurable, 3), actual);
        await Assert.That(actual.Token.Position).IsGreaterThan(0L);
    }
    private static EventData Data(int index)
    {
        string[] ids = [AggregateReplayClusterTokens.CreatedEventId, AggregateReplayClusterTokens.PaidEventId,
            AggregateReplayClusterTokens.ShippedEventId];
        return new(ids[index], AggregateReplayClusterTokens.EventType,
            "{\"increment\":" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"secret\":\"" + AggregateReplayClusterTokens.Secret + "\"}",
            "{\"privateHeader\":\"" + AggregateReplayClusterTokens.PrivateHeader + "\"}");
    }
}
