using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.GraphTraversal;

namespace KeyLoad.UnitTests.Features.Authorization;

internal static class EventingRequiredClaimCold
{
    internal static async Task RunAsync(RequiredProcessingPolicyFixture fixture, ReplicatedOperation denied, byte[] deniedOutcome,
        (ReplicatedOperation Operation, CommitReceipt Receipt, byte[] Outcome) healthy, CancellationToken token)
    {
        var db = fixture.Harness;
        var image = GraphShortestPathFullStoreImage.Bytes(db.Store);
        var position = db.Store.Position;
        var identity = db.Store.Identity;
        var bytes = NativeSerialization.Serialize(identity);
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
                    await EventingRequiredClaimInbox.AssertAsync(fixture, store, healthy.Operation, healthy.Receipt);
                    await EventingRequiredClaimState.FinalAsync(fixture, database, store, token);
                    await RequiredProcessingPolicyRefusal.ExistingAsync(database, store, db.Partition, denied, deniedOutcome,
                        ErrorCode.PermissionDenied, RequiredProcessingPolicyFixture.EpochChanged, token);
                    await RequiredProcessingPolicyAssertions.ReplayAsync(database, store, healthy.Operation, healthy.Receipt,
                        healthy.Outcome, db.Partition, token);
                    foreach (var producer in fixture.Producers)
                    {
                        await RequiredProcessingPolicyAssertions.ReplayAsync(database, store, producer.Operation,
                            producer.Receipt, producer.Outcome, db.Partition, token);
                    }
                }, failures);
                await ServerFailureObserver.ObserveAsync(() => GraphLabelUseOperations.ConserveAsync(store, image, position, bytes), failures);
            }, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
