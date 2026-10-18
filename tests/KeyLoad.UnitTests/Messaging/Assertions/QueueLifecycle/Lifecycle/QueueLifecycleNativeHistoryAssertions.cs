using KeyLoad.Core;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.ResourceExecution;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueLifecycleNativeHistoryAssertions
{
    private const string Stale = "The delivery lease is stale.";
    private const string MissingOriginal = "The genuine native lifecycle original outcome disappeared.";

    internal static async Task ReplayAsync(QueueLifecycleNativeRecorder recorder, QueueLaneRef lane, CancellationToken token)
    {
        var database = recorder.Database;
        foreach (var sample in recorder.Samples)
        {
            token.ThrowIfCancellationRequested();
            var store = (ZoneTreeStore)database.Store;
            var image = QueueWholeFlowStorage.Bytes(store);
            var position = store.Position;
            var identity = NativeSerialization.Serialize(store.Identity);
            var actual = database.Apply(sample.Operation);
            await ResultAsync(database, lane, sample, actual);
            await QueueLeaseExpiryEqualityAssertions.ColdImageAsync(store, image, position, identity);
        }
    }

    private static async Task ResultAsync(DatabaseEngine database, QueueLaneRef lane,
        QueueLifecycleNativeSample sample, Result<object> actual)
    {
        if (sample.Result.Value is ReceiveResult received)
        {
            await ReceiveAsync(database, lane, sample, actual, received);
            return;
        }
        if (Errors.Code(sample.Result.Problem) is not null)
        {
            await Assert.That(actual).IsEqualTo(sample.Result);

            await Assert.That(actual.Value).IsNull();
            return;
        }
        switch (sample.Operation.Value!.Kind)
        {
            case OperationKind.ConfigurePrincipal:
                await NativeReplayResultAssertions.Same<PrincipalRecord>(actual, sample.Result);
                break;
            case OperationKind.ConfigureResource:
                await NativeReplayResultAssertions.Same<ResourceDefinition>(actual, sample.Result);
                break;
            default:
                await NativeReplayResultAssertions.Same<CommitReceipt>(actual, sample.Result);
                break;
        }
    }

    private static async Task ReceiveAsync(DatabaseEngine database, QueueLaneRef lane,
        QueueLifecycleNativeSample sample, Result<object> actual, ReceiveResult received)
    {
        if (received.Deliveries.IsEmpty)
        {
            await NativeReplayResultAssertions.Same<ReceiveResult>(actual, sample.Result);
            return;
        }
        await Assert.That(Errors.Code(actual.Problem)).IsEqualTo(ErrorCode.StaleLease);
        await Assert.That(actual.Problem?.Detail).IsEqualTo(Stale);

        await Assert.That(actual.Value).IsNull();
        var saved = OutcomeStoreOracle.ReadPartition(database.Store, lane.Partition,
            sample.Operation.UserId!, sample.Operation.CommandId) ?? throw new InvalidOperationException(MissingOriginal);
        await NativeReplayResultAssertions.Same<ReceiveResult>(saved, sample.Result);
    }
}
