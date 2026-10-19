using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.StorageRecovery.Assertions;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class TransactionalReadViewValues
{
    internal static async Task ReceiptAsync(CommitReceipt expected, CommitReceipt actual)
    {
        await Assert.That(System.Text.Json.JsonSerializer.Serialize(actual, JsonDefaults.Options))
            .IsEqualTo(System.Text.Json.JsonSerializer.Serialize(expected, JsonDefaults.Options));
        foreach (var mutation in actual.Mutations)
        {
            await Assert.That(mutation.CompositionReferences).IsEmpty();
        }
    }
    internal static async Task ConservedAsync(TransactionalReadViewState state, string[] image, long position)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () => await Assert.That(TransactionalReadViewCommands.QueueImage(state.Store))
            .IsEquivalentTo(image, CollectionOrdering.Matching), failures);
        await ServerFailureObserver.ObserveAsync(() => ReadCutStoreStateAssertions.AssertIdentityAndPositionAsync(
            state.Store, state.Identity, position), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    internal static async Task CutAsync(TransactionalReadViewState state, ZoneTreeReadCut actual, long position)
    {
        var identity = state.Identity;
        var expected = new ZoneTreeReadCut(identity.FormatVersion, identity.KeyCodecVersion, identity.NodeId,
            identity.Incarnation, identity.Durability, identity.DispatchPaused, identity.ReadGeneration, position);
        await Assert.That(actual).IsEqualTo(expected);
    }
    internal static string[] Read(ZoneTreeReadCutLease lease)
    {
        var rows = new List<string>();
        var result = lease.VisitPrefix([], (key, value) =>
        {
            rows.Add(Convert.ToHexString(key) + ":" + Convert.ToHexString(value));
            return true;
        });
        if (result.Records != rows.Count || result.HasMore || result.StoppedByVisitor)
        {
            throw new InvalidOperationException("The transactional snapshot did not deliver its complete original cut.");
        }
        return rows.ToArray();
    }
}
