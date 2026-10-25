using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.BackupRestore;

internal static class RestoreResumedTokenAssertions
{
    private const string InvalidSignedToken = "The signed token is invalid.";
    private const long OutcomeStep = 1;

    internal static async Task<(Command<DatabaseCommand> Operation, Result<object> Result)[]> RefuseAsync(
        EventingArtifactFixture fixture)
    {
        var queueId = Guid.NewGuid();
        var groupId = Guid.NewGuid();
        var operations = new[]
        {
            fixture.Operation(OperationKind.Delivery,
                new DeliveryCommand(queueId, fixture.Lane, fixture.QueueClaim.Deliveries[0].Token,
                    DeliveryAction.Ack), queueId),
            fixture.Operation(OperationKind.SubscriptionDelivery,
                new SubscriptionDeliveryCommand(groupId, fixture.Group, fixture.Received.Deliveries[1].Token,
                    DeliveryAction.Ack), groupId)
        };
        var saved = new List<(Command<DatabaseCommand> Operation, Result<object> Result)>();
        foreach (var operation in operations)
        {
            var before = EventingArtifactState.Bytes(fixture.Database.Store, fixture.Source.Partition);
            var position = fixture.Database.Store.Position;
            var result = fixture.Database.Apply(operation);
            await Assert.That(result).IsEqualTo(Result<object>.Fail(Errors.Problem(ErrorCode.TokenInvalidated, InvalidSignedToken)));
            await Assert.That(result.Value).IsNull();
            await Assert.That(fixture.Database.Store.Position).IsEqualTo(position + OutcomeStep);
            await EventingArtifactState.SameAsync(before, fixture.Database.Store, fixture.Source.Partition);
            saved.Add((operation, result));
        }
        return saved.ToArray();
    }

    internal static async Task ReplayAsync(EventingArtifactFixture fixture,
        (Command<DatabaseCommand> Operation, Result<object> Result)[] saved)
    {
        var before = EventingArtifactState.FullBytes(fixture.Database.Store);
        var position = fixture.Database.Store.Position;
        foreach (var original in saved)
        {
            await EventingArtifactState.SameResultAsync(original.Result, fixture.Database.Apply(original.Operation));
        }
        var cursor = Assert.ThrowsExactly<KeyLoadException>(() => fixture.Database.ReadEventSource(
            EventingArtifactFixture.Principal, new(fixture.Events, Cursor: fixture.Cursor)));
        await Assert.That(cursor.Code).IsEqualTo(ErrorCode.TokenInvalidated);
        await Assert.That(fixture.Database.Store.Position).IsEqualTo(position);
        await Assert.That(EventingArtifactState.FullBytes(fixture.Database.Store))
            .IsEquivalentTo(before, CollectionOrdering.Matching);
        await Assert.That(fixture.Database.GetSubscription(EventingArtifactFixture.Principal, fixture.Group).Generation)
            .IsEqualTo(2L);
        await Assert.That(fixture.Database.GetSubscription(EventingArtifactFixture.Principal, fixture.Group).Checkpoint)
            .IsEqualTo(4L);
    }
}
