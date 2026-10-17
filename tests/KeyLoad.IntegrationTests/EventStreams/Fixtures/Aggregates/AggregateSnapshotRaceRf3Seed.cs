using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

internal static class AggregateSnapshotRaceRf3Seed
{
    internal static async Task<AggregateSnapshotRaceRf3State> RunAsync(ClusterFixture fixture,
        RequestCqrsRf3Callers root, CancellationToken token)
    {
        var scenario = await AggregateReplayRf3Scenario.CreateAsync(fixture, token);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(
            new(1, scenario.Partition), token));
        var id = Guid.NewGuid();
        var seed = await scenario.AppendInitialAsync(root.Sdk, id, token);
        var state = new AggregateSnapshotRaceRf3State(scenario, placement, seed)
        {
            SeedCommand = new(id, scenario.Partition, [new AppendEvents(AggregateReplayRf3Tokens.StreamSet,
                AggregateReplayRf3Tokens.StreamId, [Data(0), Data(1), Data(2)], ExpectedStreamRevision.NoStream)])
        };
        await ReceiptAsync(state, seed, id, new("appendEvents", AggregateReplayRf3Tokens.StreamSet,
            AggregateReplayRf3Tokens.StreamId, 3));
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
            await SqlRf3Protocol.EqualAsync(Data(index), record.Data);
        }
        state.Events = full.Events;
        state.OriginalImage = await AggregateSnapshotRaceRf3Current.ImageAsync(root, state, token);
        return state;
    }
    internal static async Task ReceiptAsync(AggregateSnapshotRaceRf3State state, CommitReceipt actual,
        Guid id, MutationReceipt mutation)
    {
        await SqlRf3Protocol.EqualAsync(new CommitReceipt(id, new(state.Placement.Incarnation,
            state.Scenario.Partition.AtomicPartitionId, actual.Token.Position, state.Placement.PlacementEpoch),
            [mutation], DurabilityProfile.QuorumProcessDurable), actual);
        await Assert.That(actual.Token.Position).IsGreaterThan(0L);
    }
    private static EventData Data(int index)
    {
        string[] ids = [AggregateReplayRf3Tokens.CreatedEventId, AggregateReplayRf3Tokens.PaidEventId,
            AggregateReplayRf3Tokens.ShippedEventId];
        return new(ids[index], AggregateReplayRf3Tokens.EventType,
            "{\"increment\":" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                + ",\"secret\":\"" + AggregateReplayRf3Tokens.Secret + "\"}",
            "{\"privateHeader\":\"" + AggregateReplayRf3Tokens.PrivateHeader + "\"}");
    }
}
