using KeyLoad.Core;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueOrderedRetryEmptyProfile
{
    internal static async Task RunAsync(string directory, ZoneTreeStore store, DatabaseEngine database,
        QueueOrderedRetryState state, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var key = KeySpace.Resource(state.Partition.TenantId, state.Partition.DatabaseId, state.Lane.Queue);
        var original = store.Read(view => view.GetRecord<ResourceDefinition>(key))!;
        var expected = original with
        {
            SchemaVersion = original.SchemaVersion + QueueOrderedRetryProtocol.One,
            QueuePolicy = original.QueuePolicy with
            {
                OrderingProfile = QueueOrderingProfile.CompetingConsumers,
                ParkedHeadPolicy = QueueParkedHeadPolicy.Continue,
                RetryJitter = QueueRetryJitter.None,
                RetryExponentialFactor = QueueOrderedRetryProtocol.Three
            }
        };
        var image = QueueOrderedRetryImage.Capture(store, state.Lane);
        var result = state.Execute(database, OperationKind.ConfigureResource, new ConfigureResourceRequest(
            state.Partition.TenantId, state.Partition.DatabaseId, expected)
        { ExpectedSchemaVersion = original.SchemaVersion }, Guid.NewGuid()).Get<ResourceDefinition>();
        await Assert.That(result).IsEqualTo(expected);
        await QueueOrderedRetryImage.SameAsync(store, state.Lane, image);
        state.Batch(database, QueueOrderedRetryOperations.Literal(QueueOrderedRetryProtocol.ProfileHealthy, null)).Get<CommitReceipt>();
        var lease = await QueueOrderedRetryOperations.ClaimAsync(database, state, QueueOrderedRetryProtocol.ProfileHealthy);
        QueueOrderedRetryOperations.Complete(database, state, lease, DeliveryAction.Ack).Get<CommitReceipt>();
        await QueueOrderedRetryAssertions.MessageAsync(database, state, new(QueueOrderedRetryProtocol.ProfileHealthy, MessageState.Acked,
            QueueOrderedRetryProtocol.One, QueueOrderedRetryProtocol.Three, QueueOrderedRetryProtocol.Seven, null, null,
            LeaseVersion: QueueOrderedRetryProtocol.One), stored: false);
        await QueueOrderedRetryOperations.ReplayAsync(database, state);
        var terminal = QueueOrderedRetryImage.Capture(store, state.Lane);
        store.Dispose();
        using var cold = new ZoneTreeStore(new(directory), UnitExecutionOptions.StorageExecution(), UnitExecutionOptions.PointCacheExecution());
        var reopened = QueueWholeFlowStorage.Open(cold);
        await Assert.That(cold.Read(view => view.GetRecord<ResourceDefinition>(key))).IsEqualTo(expected);
        await QueueOrderedRetryImage.SameAsync(cold, state.Lane, terminal);
        await QueueOrderedRetryOperations.ReplayAsync(reopened, state);
        await QueueOrderedRetryAssertions.MessageAsync(reopened, state, new(QueueOrderedRetryProtocol.ProfileHealthy, MessageState.Acked,
            QueueOrderedRetryProtocol.One, QueueOrderedRetryProtocol.Three, QueueOrderedRetryProtocol.Seven, null, null,
            LeaseVersion: QueueOrderedRetryProtocol.One), stored: false);
    }
}
