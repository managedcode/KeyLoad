using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Messaging;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class RetainedAppendIdentityFlow
{
    internal static async Task RunAsync(string kind, CancellationToken token)
    {
        RetainedAppendIdentityFixture? fixture = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            fixture = new();
            await fixture.InitializeAsync();
            await OperationAsync(fixture, kind, token);
        }, failures);
        if (fixture is not null)
        {
            ServerFailureObserver.Observe(fixture.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task OperationAsync(RetainedAppendIdentityFixture fixture, string kind, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var store = fixture.Harness.Store;
        var original = QueueWholeFlowStorage.Bytes(store);
        var position = store.Position;
        var identity = NativeReadCutStoreStateAssertions.SnapshotIdentity(store);
        var command = fixture.Producer("repaired", RetainedAppendIdentityFixture.Candidate, ExpectedStreamRevision.Exact(1));
        var fault = new RetainedAppendIdentityFault(fixture, kind);
        fault.Damage(store);
        var damaged = QueueWholeFlowStorage.Bytes(store);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => RetainedAppendIdentityOperations.RefuseAsync(fixture.Harness.Database,
            store, command, damaged, position + 1, identity), failures);
        ServerFailureObserver.Observe(store.Dispose, failures);
        await ServerFailureObserver.ObserveAsync(() => RetainedAppendIdentityCold.RepairAsync(fixture, fault, command,
            original, damaged, position, identity, token), failures);
        await ServerFailureObserver.ObserveAsync(() => RetainedAppendIdentityCold.FollowingAsync(fixture, token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
