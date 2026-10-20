using System.Collections.Immutable;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleReceiptAssertions
{
    private const int Increment = 1;
    private const int EnqueueRevision = 1;
    private const string EnqueueKind = "enqueue";
    private const string RedriveKind = "redriveQueueMessage";
    private const string CancelKind = "cancelQueueMessage";
    private const string ParkKind = "parkPendingQueueMessage";
    private const string UnsupportedFixture = "The execution lifecycle receipt fixture has an unexpected mutation.";

    internal static async Task ResultAsync(DatabaseEngine database, QueueLaneRef lane,
        Func<string, EnqueueMessage> literal, QueueLifecycleObservation observation)
    {
        if (Errors.Code(observation.Result.Problem) is not null)
        {

            await Assert.That(observation.Result.Value).IsNull();
            return;
        }
        if (observation.Result.Value is CommitReceipt)
        {
            await EqualAsync(observation.Result.Get<CommitReceipt>(), new CommitReceipt(observation.Operation.CommandId, observation.ExpectedToken, ExpectedMutations(database, lane, observation), DurabilityProfile.ProcessDurable, 1));
        }
        else if (observation.Result.Value is ReceiveResult result)
        {
            await ReceiveAsync(database, lane, literal, observation, result);
        }
    }

    private static ImmutableArray<MutationReceipt> ExpectedMutations(DatabaseEngine database, QueueLaneRef lane,
        QueueLifecycleObservation observation)
    {
        if (observation.Operation.Value!.Kind == OperationKind.Delivery)
        {
            var request = OrleansSerialization.Deserialize<DeliveryCommand>(observation.Operation.Value!.Input.Span, ValidationProfile.PublicInputElements);
            var claims = database.Verify<DeliveryClaims>(request.Token);
            return [new(request.Action.ToString(), lane.Queue, claims.MessageId,
                checked(observation.Before[claims.MessageId].StateVersion + Increment))];
        }
        var batch = OrleansSerialization.Deserialize<CommandRequest>(observation.Operation.Value!.Input.Span, ValidationProfile.PublicInputElements);
        var expected = ImmutableArray.CreateBuilder<MutationReceipt>();
        foreach (var mutation in batch.Mutations)
        {
            expected.Add(mutation switch
            {
                EnqueueMessage message => new(EnqueueKind, message.Queue, message.MessageId, EnqueueRevision),
                RedriveQueueMessage redrive => new(RedriveKind, redrive.Queue, redrive.MessageId,
                    checked(observation.Before[redrive.MessageId].StateVersion + Increment)),
                CancelQueueMessage cancel => new(CancelKind, cancel.Queue, cancel.MessageId,
                    checked(observation.Before[cancel.MessageId].StateVersion + Increment)),
                ParkPendingQueueMessage park => new(ParkKind, park.Queue, park.MessageId,
                    checked(observation.Before[park.MessageId].StateVersion + Increment)),
                _ => throw new InvalidOperationException(UnsupportedFixture)
            });
        }
        return expected.ToImmutable();
    }

    private static async Task ReceiveAsync(DatabaseEngine database, QueueLaneRef lane,
        Func<string, EnqueueMessage> literal, QueueLifecycleObservation observation, ReceiveResult actual)
    {
        var request = OrleansSerialization.Deserialize<ReceiveRequest>(observation.Operation.Value!.Input.Span, ValidationProfile.PublicInputElements);
        var expected = ImmutableArray.CreateBuilder<Delivery>();
        foreach (var delivery in actual.Deliveries)
        {
            var before = observation.Before[delivery.Id];
            var body = literal(delivery.Id);
            var version = checked(before.LeaseVersion + Increment);
            var claims = new DeliveryClaims(lane, delivery.Id, observation.Operation.UserId!, version,
                before.DeliveryGeneration, observation.ExpectedToken.Incarnation);
            expected.Add(new(delivery.Id, body.PayloadJson, body.HeadersJson, database.Sign(claims), version,
                new DateTimeOffset(observation.Operation.Timestamp).AddSeconds(request.LeaseSeconds), checked(before.Attempts + Increment),
                before.DeliveryGeneration));
            await EqualAsync(database.Verify<DeliveryClaims>(delivery.Token), claims);
        }
        await EqualAsync(actual, new ReceiveResult(request.RequestId, expected.ToImmutable(), observation.ExpectedToken));
    }

    private static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(OrleansSerialization.Serialize(actual).AsSpan()
            .SequenceEqual(OrleansSerialization.Serialize(expected))).IsTrue();
}
