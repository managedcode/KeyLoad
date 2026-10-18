using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleNativeReceiptAssertions
{
    private const int Increment = 1;
    private const int EnqueueRevision = 1;
    private const string EnqueueKind = "enqueue";
    private const string RedriveKind = "redriveQueueMessage";
    private const string CancelKind = "cancelQueueMessage";
    private const string ParkKind = "parkPendingQueueMessage";
    private const string UnsupportedFixture = "The native lifecycle receipt fixture has an unexpected mutation.";

    internal static async Task ResultAsync(DatabaseEngine database, QueueLaneRef lane,
        Func<string, EnqueueMessage> literal, QueueLifecycleNativeSample sample)
    {
        if (Errors.Code(sample.Result.Problem) is not null)
        {

            await Assert.That(sample.Result.Value).IsNull();
            return;
        }
        if (sample.Result.Value is CommitReceipt)
        {
            await EqualAsync(sample.Result.Get<CommitReceipt>(), new CommitReceipt(sample.Operation.CommandId, sample.ExpectedToken, ExpectedMutations(database, lane, sample), DurabilityProfile.ProcessDurable, 1));
        }
        else if (sample.Result.Value is ReceiveResult result)
        {
            await ReceiveAsync(database, lane, literal, sample, result);
        }
    }

    private static ImmutableArray<MutationReceipt> ExpectedMutations(DatabaseEngine database, QueueLaneRef lane,
        QueueLifecycleNativeSample sample)
    {
        if (sample.Operation.Value!.Kind == OperationKind.Delivery)
        {
            var request = NativeSerialization.Deserialize<DeliveryCommand>(sample.Operation.Value!.Input.Span, NativeValidationProfile.PublicInputElements);
            var claims = database.Verify<DeliveryClaims>(request.Token);
            return [new(request.Action.ToString(), lane.Queue, claims.MessageId,
                checked(sample.Before[claims.MessageId].StateVersion + Increment))];
        }
        var batch = NativeSerialization.Deserialize<CommandRequest>(sample.Operation.Value!.Input.Span, NativeValidationProfile.PublicInputElements);
        var expected = ImmutableArray.CreateBuilder<MutationReceipt>();
        foreach (var mutation in batch.Mutations)
        {
            expected.Add(mutation switch
            {
                EnqueueMessage message => new(EnqueueKind, message.Queue, message.MessageId, EnqueueRevision),
                RedriveQueueMessage redrive => new(RedriveKind, redrive.Queue, redrive.MessageId,
                    checked(sample.Before[redrive.MessageId].StateVersion + Increment)),
                CancelQueueMessage cancel => new(CancelKind, cancel.Queue, cancel.MessageId,
                    checked(sample.Before[cancel.MessageId].StateVersion + Increment)),
                ParkPendingQueueMessage park => new(ParkKind, park.Queue, park.MessageId,
                    checked(sample.Before[park.MessageId].StateVersion + Increment)),
                _ => throw new InvalidOperationException(UnsupportedFixture)
            });
        }
        return expected.ToImmutable();
    }

    private static async Task ReceiveAsync(DatabaseEngine database, QueueLaneRef lane,
        Func<string, EnqueueMessage> literal, QueueLifecycleNativeSample sample, ReceiveResult actual)
    {
        var request = NativeSerialization.Deserialize<ReceiveRequest>(sample.Operation.Value!.Input.Span, NativeValidationProfile.PublicInputElements);
        var expected = ImmutableArray.CreateBuilder<Delivery>();
        foreach (var delivery in actual.Deliveries)
        {
            var before = sample.Before[delivery.Id];
            var body = literal(delivery.Id);
            var version = checked(before.LeaseVersion + Increment);
            var claims = new DeliveryClaims(lane, delivery.Id, sample.Operation.UserId!, version,
                before.DeliveryGeneration, sample.ExpectedToken.Incarnation);
            expected.Add(new(delivery.Id, body.PayloadJson, body.HeadersJson, database.Sign(claims), version,
                new DateTimeOffset(sample.Operation.Timestamp).AddSeconds(request.LeaseSeconds), checked(before.Attempts + Increment),
                before.DeliveryGeneration));
            await EqualAsync(database.Verify<DeliveryClaims>(delivery.Token), claims);
        }
        await EqualAsync(actual, new ReceiveResult(request.RequestId, expected.ToImmutable(), sample.ExpectedToken));
    }

    private static async Task EqualAsync<T>(T actual, T expected)
        => await Assert.That(NativeSerialization.Serialize(actual).AsSpan()
            .SequenceEqual(NativeSerialization.Serialize(expected))).IsTrue();
}
