using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleHistoryAssertions
{
    private const string Stale = "The delivery lease is stale.";
    private const string MissingOriginal = "The genuine execution lifecycle original outcome disappeared.";

    internal static async Task ReplayAsync(QueueLifecycleRecorder recorder, QueueLaneRef lane, CancellationToken token)
    {
        var database = recorder.Database;
        foreach (var observation in recorder.Observations)
        {
            token.ThrowIfCancellationRequested();
            var store = (ZoneTreeStore)database.Store;
            var image = QueueWholeFlowStorage.Bytes(store);
            var position = store.Position;
            var identity = OrleansSerialization.Serialize(store.Identity);
            var actual = database.Apply(observation.Operation);
            await ResultAsync(database, lane, observation, actual);
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
        }
    }

    private static async Task ResultAsync(DatabaseEngine database, QueueLaneRef lane,
        QueueLifecycleObservation observation, Result<object> actual)
    {
        if (observation.Result.Value is ReceiveResult received)
        {
            await ReceiveAsync(database, lane, observation, actual, received);
            return;
        }
        if (Errors.Code(observation.Result.Problem) is not null)
        {
            await Assert.That(OrleansSerialization.Serialize(actual).AsSpan()
                .SequenceEqual(OrleansSerialization.Serialize(observation.Result))).IsTrue();

            await Assert.That(actual.Value).IsNull();
            return;
        }
        switch (observation.Operation.Value!.Kind)
        {
            case OperationKind.ConfigurePrincipal:
                await ReplayResultAssertions.Same<PrincipalRecord>(actual, observation.Result);
                break;
            case OperationKind.ConfigureResource:
                await ReplayResultAssertions.Same<ResourceDefinition>(actual, observation.Result);
                break;
            default:
                await ReplayResultAssertions.Same<CommitReceipt>(actual, observation.Result);
                break;
        }
    }

    private static async Task ReceiveAsync(DatabaseEngine database, QueueLaneRef lane,
        QueueLifecycleObservation observation, Result<object> actual, ReceiveResult received)
    {
        if (received.Deliveries.IsEmpty)
        {
            await ReplayResultAssertions.Same<ReceiveResult>(actual, observation.Result);
            return;
        }
        await Assert.That(Errors.Code(actual.Problem)).IsEqualTo(ErrorCode.StaleLease);
        await Assert.That(actual.Problem?.Detail).IsEqualTo(Stale);

        await Assert.That(actual.Value).IsNull();
        var saved = OutcomeStoreOracle.ReadPartition(database.Store, lane.Partition,
            observation.Operation.UserId!, observation.Operation.CommandId) ?? throw new InvalidOperationException(MissingOriginal);
        await ReplayResultAssertions.Same<ReceiveResult>(saved, observation.Result);
    }
}
