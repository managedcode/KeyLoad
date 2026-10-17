using KeyLoad.Server;
using KeyLoad.UnitTests.Features.Authorization;
using static KeyLoad.UnitTests.Features.Messaging.RecurringSagaSchemaProtocol;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RecurringSagaSchemaCold
{
    internal static async Task RunAsync(SafeProjectorOperationDatabase owner, RecurringSagaSchemaState state)
    {
        await Assert.That(state.Reader.PolicyEpoch).IsEqualTo(FinalEpoch);
        await ReplayAsync(owner, state);
        for (var cold = Zero; cold < ColdOwners; cold++)
        {
            var image = QueueWholeFlowStorage.Bytes(owner.Store);
            var position = owner.Store.Position;
            var applied = owner.Database.LastApplied;
            var journal = owner.JournalCut();
            var identity = NativeSerialization.Serialize(owner.Store.Identity);
            await owner.ReopenAsync();
            var failures = new List<Exception>();
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await Assert.That(QueueWholeFlowStorage.Bytes(owner.Store).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
                await Assert.That(owner.Store.Position).IsEqualTo(position);
                await Assert.That(owner.Database.LastApplied).IsEqualTo(applied);
                await Assert.That(owner.JournalCut()).IsEqualTo(journal);
                await Assert.That(NativeSerialization.Serialize(owner.Store.Identity).AsSpan().SequenceEqual(identity)).IsTrue();
            }, failures);
            await ServerFailureObserver.ObserveAsync(() => RecurringSagaSchemaCanonical.CurrentAsync(owner, state), failures);
            await ServerFailureObserver.ObserveAsync(() => RecurringSagaSchemaProjection.AllAsync(owner, state,
                bodySchema: false, headerSchema: false), failures);
            await ServerFailureObserver.ObserveAsync(() => ReplayAsync(owner, state), failures);
            ServerFailureObserver.ThrowIfAny(failures);
        }
    }

    private static Task ReplayAsync(SafeProjectorOperationDatabase owner, RecurringSagaSchemaState state)
        => RecurringSagaSchemaReplay.AllAsync(owner, state);
}
