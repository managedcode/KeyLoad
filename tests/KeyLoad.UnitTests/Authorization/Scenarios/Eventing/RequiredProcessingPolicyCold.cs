using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.GraphTraversal;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Authorization;

internal static class RequiredProcessingPolicyCold
{
    internal static async Task RunAsync(RequiredProcessingPolicyFixture fixture,
        (Command<DatabaseCommand> Operation, byte[] Outcome) refused,
        (Command<DatabaseCommand> Operation, CommitReceipt Receipt, byte[] Outcome) original,
        (Command<DatabaseCommand> Operation, CommitReceipt Receipt, byte[] Outcome) healthy, CancellationToken token)
    {
        var db = fixture.Harness;
        var image = CanonicalStoreImage.Bytes(db.Store);
        var position = db.Store.Position;
        var identity = db.Store.Identity;
        var bytes = OrleansSerialization.Serialize(identity);
        db.JoinOwnedResources();
        var failures = new List<Exception>();
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var store = ZoneTreeExistingStore.Open(new(db.Directory)
                {
                    Incarnation = identity.Incarnation
                }, identity.NodeId, db.StorageExecution);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var database = db.CreateConfiguredDatabase(store);
                    await RequiredProcessingPolicyReads.FinalAsync(fixture, database, store, token);
                    await RequiredProcessingPolicyAssertions.ReplayAsync(database, store, healthy.Operation, healthy.Receipt,
                        healthy.Outcome, db.Partition, token);
                    await RequiredProcessingPolicyRefusal.ExistingAsync(database, store, db.Partition, original.Operation,
                        original.Outcome, ErrorCode.PermissionDenied, RequiredProcessingPolicyFixture.EpochChanged, token);
                    await RequiredProcessingPolicyRefusal.ExistingAsync(database, store, db.Partition, refused.Operation,
                        refused.Outcome, ErrorCode.PermissionDenied, RequiredProcessingPolicyFixture.EpochChanged, token);
                    foreach (var producer in fixture.Producers)
                    {
                        await RequiredProcessingPolicyAssertions.ReplayAsync(database, store, producer.Operation, producer.Receipt,
                            producer.Outcome, db.Partition, token);
                    }
                }, failures);
                await ServerFailureObserver.ObserveAsync(() => GraphLabelUseOperations.ConserveAsync(store, image, position, bytes), failures);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
