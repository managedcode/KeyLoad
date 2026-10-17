using System.Collections.Immutable;
using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class TargetInboxCapacityAuthorityCompletion
{
    private const int OutputCount = 2;
    private const int FirstReadySequence = 1;
    private const int PlacementContractVersion = 1;
    private const int NextCommitPosition = 1;
    private const int PresenceLimit = 1;
    private const long EmptyUsage = 0L;
    private const int InitialAttempt = 1;
    private const int InitialLeaseVersion = 1;
    private const int OriginalGeneration = 1;
    private const long FinalVersion = 3L;
    private const string EmptyHeaders = "{}";
    private const string AckKind = "Ack";
    private const string MetadataFamily = "message-meta";
    private const string CountersFamily = "queue-counters";
    private static readonly string[] MessageIds =
        [TargetInboxUnitProtocol.Message, TargetInboxCapacityAuthorityFixture.Healthy];
    private static readonly string[] EmptyFamilies =
        ["ready", "scheduled", "lease", "message-body", "dead-letter", "queue-pending-dead-letter",
            "queue-dead-letter-order", "queue-order", "inbox"];

    internal static async Task RunAsync(DatabaseEngine database, ZoneTreeStore store,
        TargetInboxCapacityAuthorityState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var output = new QueueLaneRef(state.Original.Target.Partition, TargetInboxUnitProtocol.Output);
        var receive = new ReceiveRequest(Guid.NewGuid(), output, MaxMessages: OutputCount);
        var time = database.EvaluationClock.GetUtcNow();
        var expectedToken = NextToken(database, store, output);
        var received = QueueWholeFlowStorage.Apply(database, OperationKind.Receive, receive,
            receive.RequestId, time).Get<ReceiveResult>();
        var expected = new ReceiveResult(receive.RequestId, MessageIds.Select(id =>
        {
            var claims = new DeliveryClaims(output, id, TargetInboxUnitProtocol.Root, InitialLeaseVersion,
                OriginalGeneration, expectedToken.Incarnation);
            return new Delivery(id, TargetInboxUnitProtocol.Payload, EmptyHeaders, database.Sign(claims),
                InitialLeaseVersion, time.AddSeconds(receive.LeaseSeconds), InitialAttempt, OriginalGeneration);
        }).ToImmutableArray(), expectedToken);
        await ExactAsync(received, expected);
        foreach (var delivery in received.Deliveries)
        {
            token.ThrowIfCancellationRequested();
            await ExactAsync(database.Verify<DeliveryClaims>(delivery.Token), new DeliveryClaims(output,
                delivery.Id, TargetInboxUnitProtocol.Root, InitialLeaseVersion, OriginalGeneration, expectedToken.Incarnation));
            await AckAsync(database, store, output, delivery);
        }
        await TerminalAsync(store, output);
        await TargetInboxNativeAssertions.CapacityAsync(store, state.Original, TargetInboxCapacityAuthorityFixture.Capacity);
        await TargetInboxCapacityAuthorityAssertions.HistoryAsync(database, store, state, token);
    }

    private static async Task AckAsync(DatabaseEngine database, ZoneTreeStore store, QueueLaneRef output, Delivery delivery)
    {
        var ack = new DeliveryCommand(Guid.NewGuid(), output, delivery.Token, DeliveryAction.Ack);
        var expected = new CommitReceipt(ack.CommandId, NextToken(database, store, output),
            [new(AckKind, output.Queue, delivery.Id, FinalVersion)], DurabilityProfile.ProcessDurable);
        var result = QueueWholeFlowStorage.Apply(database, OperationKind.Delivery, ack,
            ack.CommandId, database.EvaluationClock.GetUtcNow());
        await ExactAsync(result.Get<CommitReceipt>(), expected);
        var sequence = Array.IndexOf(MessageIds, delivery.Id) + FirstReadySequence;
        await ExactAsync(database.InspectMessage(TargetInboxUnitProtocol.Root, output, delivery.Id),
            new MessageInspection(new(delivery.Id, MessageState.Acked, InitialAttempt, FinalVersion,
                sequence, null, null, LeaseVersion: InitialLeaseVersion, DeliveryGeneration: OriginalGeneration), null, null));
    }

    private static async Task TerminalAsync(ZoneTreeStore store, QueueLaneRef output)
    {
        var counters = store.Read(view => view.GetRecord<QueueCounters>(KeySpace.Partition(CountersFamily, output.Partition, output.Queue)));
        await ExactAsync(counters, new QueueCounters(EmptyUsage, EmptyUsage, EmptyUsage, EmptyUsage, OutputCount));
        var metadata = store.Read(view => view.Scan(KeySpace.Partition(MetadataFamily, output.Partition, output.Queue), OutputCount + PresenceLimit));
        await Assert.That(metadata.Records.Length).IsEqualTo(OutputCount);
        await Assert.That(metadata.HasMore).IsFalse();
        foreach (var family in EmptyFamilies)
        {
            var page = store.Read(view => view.Scan(KeySpace.Partition(family, output.Partition, output.Queue), PresenceLimit));
            await Assert.That(page.Records).IsEmpty();
            await Assert.That(page.HasMore).IsFalse();
        }
    }

    private static CommitToken NextToken(DatabaseEngine database, ZoneTreeStore store, QueueLaneRef output)
    {
        var placement = database.ReadAtomicPartitionPlacement(TargetInboxUnitProtocol.Root, new(PlacementContractVersion, output.Partition));
        return new(placement.Incarnation, output.Partition.AtomicPartitionId,
            checked(store.Position + NextCommitPosition), placement.PlacementEpoch);
    }

    private static async Task ExactAsync<T>(T actual, T expected)
        => await TargetInboxNativeValueAssertions.EqualAsync(actual, expected);
}
