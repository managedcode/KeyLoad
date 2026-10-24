using System.Text.Json;

namespace KeyLoad.UnitTests.Messaging;

internal static class QueueStrictHeadBodyWholeFlow
{
    internal static async Task RunAsync(MessageState headState, CancellationToken token)
    {
        var failures = new List<Exception>();
        try
        {
            token.ThrowIfCancellationRequested();
            using var fixture = new TestDatabase();
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                var state = await SeedAsync(fixture, headState);
                await QueueStrictHeadBodyContinuation.RunAsync(fixture, state, failures, token);
            }, failures);
        }
        catch (Exception error) when (global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!global::KeyLoad.Clustering.CqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<QueueStrictHeadBodyState> SeedAsync(TestDatabase fixture, MessageState headState)
    {
        if (headState is not (MessageState.Scheduled or MessageState.Leased))
        {
            throw new ArgumentOutOfRangeException(nameof(headState));
        }
        fixture.Configure(QueueOrderedRetryProtocol.Queue, ResourceKind.WorkQueue,
            queuePolicy: new()
            {
                OrderingProfile = QueueOrderingProfile.StrictPerKey
            });
        var state = new QueueOrderedRetryState(fixture.Partition,
            QueueWholeFlowStorage.Clock(fixture.Store), QueueParkedHeadPolicy.Block);
        var scheduledAt = state.Time.AddSeconds(60);
        var literal = QueueOrderedRetryOperations.Literal(QueueOrderedRetryProtocol.First) with
        {
            NotBefore = headState == MessageState.Scheduled ? scheduledAt : null
        };
        state.Batch(fixture.Database, literal, QueueOrderedRetryOperations.Literal(QueueOrderedRetryProtocol.Second),
            QueueOrderedRetryOperations.Literal(QueueOrderedRetryProtocol.Other, QueueOrderedRetryProtocol.OtherKey)).Get<CommitReceipt>();
        if (headState == MessageState.Leased)
        {
            state.FirstLease = await QueueOrderedRetryOperations.ClaimAsync(fixture.Database, state, QueueOrderedRetryProtocol.First);
            await QueueStrictHeadBodyAssertions.DeliveryAsync(state.FirstLease, QueueOrderedRetryProtocol.First, state.Time);
            await QueueStrictHeadBodyAssertions.ReplayAsync(fixture.Database, fixture.Store, state, QueueOrderedRetryProtocol.First);
        }
        var key = KeySpace.Partition("message-body", state.Partition, state.Lane.Queue, QueueOrderedRetryProtocol.First);
        var row = fixture.Store.Read(view => view.ReadOwnedValue(key))!;
        var expectedBody = new MessageBody(literal.MessageId, literal.PayloadJson, literal.HeadersJson,
            literal.OrderingKey, JsonData.Fingerprint(literal));
        await Assert.That(row.SequenceEqual(OrleansSerialization.Serialize(expectedBody))).IsTrue();
        var request = new ReceiveRequest(Guid.NewGuid(), state.Lane, MaxMessages: 1, LeaseSeconds: 30);
        var operation = fixture.Database.OwnCommand(fixture.Database.CreateCommand(OperationKind.Receive, request.RequestId, state.Principal, state.Time, JsonSerializer.Serialize(request, JsonDefaults.Options)));
        operation = fixture.Database.PrepareQueueRetryOperation(operation);
        var original = QueueLifecycleConditionAssertions.Capture(fixture.Store);
        QueueLifecycleConditionOperations.Restore(fixture.Store, key,
            OrleansSerialization.Serialize(expectedBody with
            {
                Id = "foreign-strict-head"
            }));
        var damaged = QueueLifecycleConditionAssertions.Capture(fixture.Store);
        await Assert.That(damaged.Position).IsEqualTo(original.Position + 1);
        var result = new QueueStrictHeadBodyState(state, headState, scheduledAt, key, row, operation, original, damaged);
        await QueueStrictHeadBodyAssertions.RefusalAsync(fixture.Database, fixture.Store, result);
        return result;
    }
}
