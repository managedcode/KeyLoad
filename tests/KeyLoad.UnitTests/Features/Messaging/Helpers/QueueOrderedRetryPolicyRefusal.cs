using KeyLoad.Core;

using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueOrderedRetryPolicyRefusal
{
    internal static async Task RunAsync(DatabaseEngine database, QueueOrderedRetryState state)
    {
        var key = KeySpace.Resource(state.Partition.TenantId, state.Partition.DatabaseId, state.Lane.Queue);
        var original = database.Store.Read(view => view.GetRecord<ResourceDefinition>(key))!;
        var replacement = original with
        {
            SchemaVersion = original.SchemaVersion + QueueOrderedRetryProtocol.One,
            QueuePolicy = original.QueuePolicy with
            {
                OrderingProfile = QueueOrderingProfile.CompetingConsumers,
                ParkedHeadPolicy = QueueParkedHeadPolicy.Continue
            }
        };
        var before = QueueOrderedRetryImage.Capture(database.Store, state.Lane);
        var failed = state.Execute(database, OperationKind.ConfigureResource,
            new ConfigureResourceRequest(state.Partition.TenantId, state.Partition.DatabaseId, replacement)
            { ExpectedSchemaVersion = original.SchemaVersion }, Guid.NewGuid());
        await Assert.That(failed.Error).IsEqualTo(ErrorCode.Conflict);
        await Assert.That(database.Store.Read(view => view.GetRecord<ResourceDefinition>(key))).IsEqualTo(original);
        await QueueOrderedRetryImage.SameAsync(database.Store, state.Lane, before);
    }
}
