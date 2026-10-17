using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using P = KeyLoad.UnitTests.Features.Authorization.FieldWriteOutputProtocol;

namespace KeyLoad.UnitTests.Features.Authorization;

internal sealed class FieldWriteOutputLifetime
{
    private bool originalAttempted;
    private bool originalClosed;
    private bool uncertain;
    private ZoneTreeStore? cold;

    internal async Task ColdAsync(FieldWriteOutputState state, CancellationToken token)
    {
        var image = QueueWholeFlowStorage.Bytes(state.Store);
        var position = state.Store.Position;
        var identity = state.Store.Identity;
        var failures = new List<Exception>();
        CloseOriginal(state.Owner, failures);
        for (var cycle = 0; cycle < P.ColdCount && originalClosed && !uncertain; cycle++)
        {
            ServerFailureObserver.Observe(() => cold = ZoneTreeExistingStore.Open(new(state.Owner.Directory)
            {
                Incarnation = identity.Incarnation
            }, identity.NodeId, state.Owner.StorageExecution), failures);
            if (cold is null)
            {
                uncertain = true;
                break;
            }
            state.Store = cold;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                state.Database = state.Owner.CreateConfiguredDatabase(cold);
                await FieldWriteOutputAssertions.EqualAsync(identity.NodeId, cold.Identity.NodeId);
                await FieldWriteOutputAssertions.EqualAsync(identity.Incarnation, cold.Identity.Incarnation);
                await Assert.That(cold.Identity.ReadGeneration).IsGreaterThanOrEqualTo(identity.ReadGeneration);
                await FieldWriteOutputAssertions.UnchangedAsync(state, image, position);
                await FieldWriteOutputAssertions.CurrentAsync(state, true, token);
                await FieldWriteOutputAssertions.ReplayAsync(state, token);
            }, failures);
            CloseCold(failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private void CloseOriginal(TestDatabase db, List<Exception> failures)
    {
        if (originalAttempted)
        {
            return;
        }
        originalAttempted = true;
        ServerFailureObserver.Observe(() =>
        {
            db.JoinOwnedResources();
            originalClosed = true;
        }, failures);
        uncertain |= !originalClosed;
    }

    private void CloseCold(List<Exception> failures)
    {
        var closed = false;
        ServerFailureObserver.Observe(() =>
        {
            cold!.Dispose();
            closed = true;
        }, failures);
        if (closed)
        {
            cold = null;
        }
        uncertain |= !closed;
    }

    internal void Cleanup(TestDatabase db, List<Exception> failures)
    {
        CloseOriginal(db, failures);
        if (originalClosed && !uncertain && cold is null)
        {
            ServerFailureObserver.Observe(() => Directory.Delete(db.Directory, recursive: true), failures);
        }
    }
}
