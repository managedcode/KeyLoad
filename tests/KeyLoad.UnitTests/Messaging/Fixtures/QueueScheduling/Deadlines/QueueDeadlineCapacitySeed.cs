using KeyLoad.Core;
using KeyLoad.Storage;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using static KeyLoad.UnitTests.Features.Messaging.QueueDeadlineCapacityProtocol;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineCapacitySeed
{
    internal static async Task<QueueDeadlineCapacityState> RunAsync(TestDatabase fixture, QueueDeadlineKind kind,
        bool sweep, bool jitter, QueueDeadlineCapacityFault fault, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        fixture.Configure(Queue, ResourceKind.WorkQueue, queuePolicy: new()
        {
            RetryJitter = jitter ? QueueRetryJitter.Full : QueueRetryJitter.None,
            RetryBaseMilliseconds = RetryCapMilliseconds,
            MaxAttempts = fault is QueueDeadlineCapacityFault.ParkedSequence or QueueDeadlineCapacityFault.FullDeadLetterMaxPark
                or QueueDeadlineCapacityFault.ExpiryBeforeDeadLetterMaxPark ? AttemptsBeforeDeadLetter : UnexhaustedAttemptLimit,
            MaxDeadLetterBytes = fault == QueueDeadlineCapacityFault.FullDeadLetterMaxPark ? InsufficientDeadLetterBytes : null
        });
        var lane = new QueueLaneRef(fixture.Partition, Queue);
        var start = QueueWholeFlowStorage.Clock(fixture.Store);
        var due = start.AddSeconds(LeaseSeconds);
        var input = new EnqueueMessage(Queue, Message, QueueDeadlineCapacityProtocol.Json, QueueDeadlineCapacityProtocol.Json,
            NotBefore: kind == QueueDeadlineKind.PromoteScheduled ? due : null,
            ExpiresAt: kind == QueueDeadlineKind.ExpireMessage || fault == QueueDeadlineCapacityFault.ExpiryBeforeDeadLetterMaxPark ? due : null);
        var command = new CommandRequest(Guid.NewGuid(), lane.Partition, [input]);
        var operation = QueueDeadlinePoisonPrefixClaims.Operation(fixture.Database, OperationKind.Batch,
            command.CommandId, command, Root, start);
        var expected = new CommitReceipt(command.CommandId, QueueDeadlinePoisonPrefixClaims.NextToken(fixture.Database, fixture.Store, lane), [new(EnqueueReceipt, Queue, Message, InitialStateVersion)], DurabilityProfile.ProcessDurable, 1);
        var result = fixture.Database.Apply(operation);
        await QueueDeadlineCapacityOperations.ReceiptAsync(result, expected);
        List<(Command<DatabaseCommand> Operation, Result<object> Result)> outcomes = [(operation, result)];
        var metadata = new MessageMetadata(Message, input.NotBefore is null ? MessageState.Ready : MessageState.Scheduled,
            NoAttempts, InitialStateVersion, input.NotBefore is null ? InitialReadySequence : NoReadySequence, input.NotBefore, input.ExpiresAt);
        if (kind == QueueDeadlineKind.ExpireLease)
        {
            var received = await QueueDeadlinePoisonPrefixClaims.ReceiveAsync(fixture.Database, fixture.Store, lane,
                [metadata], Root, start, token);
            var request = new ReceiveRequest(received.RequestId, lane, MaxMessages: SingleReceiveMessage);
            outcomes.Add((QueueDeadlinePoisonPrefixClaims.Operation(fixture.Database, OperationKind.Receive,
                request.RequestId, request, Root, start), Result<object>.Succeed(received)));
            metadata = metadata with
            {
                State = MessageState.Leased,
                Attempts = FirstAttempt,
                StateVersion = InitialClaimedStateVersion,
                LeaseOwner = Root,
                LeaseVersion = InitialLeaseVersion,
                LeaseUntil = due
            };
        }
        var body = new MessageBody(Message, QueueDeadlineCapacityProtocol.Json, new string(QueueDeadlineCapacityProtocol.Json.AsSpan()), null, JsonData.Fingerprint(input));
        var bytes = OrleansSerialization.Serialize(body).LongLength;
        var counters = new QueueCounters(SingleMessageCount, bytes, kind == QueueDeadlineKind.ExpireLease ? SingleMessageCount : NoUsage,
            kind == QueueDeadlineKind.ExpireLease ? bytes : NoUsage, metadata.ReadySequence);
        await QueueDeadlineCapacityImage.RequireAsync(fixture.Database, lane, [(input, metadata)], counters.NextReadySequence);
        return Damage(fixture, lane, start, due, input, metadata, counters, kind, sweep, jitter, fault, outcomes);
    }

    private static QueueDeadlineCapacityState Damage(TestDatabase fixture, QueueLaneRef lane, DateTimeOffset start,
        DateTimeOffset due, EnqueueMessage input, MessageMetadata original, QueueCounters counters, QueueDeadlineKind kind,
        bool sweep, bool jitter, QueueDeadlineCapacityFault fault, List<(Command<DatabaseCommand> Operation, Result<object> Result)> outcomes)
    {
        var key = fault == QueueDeadlineCapacityFault.StateVersion
            ? KeySpace.Partition(Metadata, lane.Partition, Queue, Message) : KeySpace.Partition(Counters, lane.Partition, Queue);
        var bytes = fixture.Store.Commit((transaction, _) =>
        {
            var retained = transaction.ReadOwnedValue(key) ?? throw new InvalidOperationException(MissingFaultRow);
            if (fault == QueueDeadlineCapacityFault.StateVersion)
            {
                transaction.PutRecord(key, original with
                {
                    StateVersion = long.MaxValue
                });
            }
            else if (fault == QueueDeadlineCapacityFault.ReadySequence)
            {
                transaction.PutRecord(key, counters with
                {
                    NextReadySequence = long.MaxValue
                });
            }
            else
            {
                transaction.PutRecord(key, counters with
                {
                    NextParkedSequence = long.MaxValue
                });
            }
            return retained;
        });
        var metadata = fault == QueueDeadlineCapacityFault.StateVersion ? original with
        {
            StateVersion = long.MaxValue
        } : original;
        var operation = QueueDeadlineCapacityOperations.Due(fixture.Database, lane, metadata, due, kind, sweep);
        return new(lane, start, due, input, original, counters, sweep, jitter, kind, fault, key, bytes, operation, outcomes);
    }
}
