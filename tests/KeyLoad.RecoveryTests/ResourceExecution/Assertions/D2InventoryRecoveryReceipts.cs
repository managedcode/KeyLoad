using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.ResourceExecution;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2InventoryRecoveryReceipts
{
    internal static async Task<CommitReceipt> RequireSelectedAsync(ControlledPartitionMovementNativeNode source,
        D2InventoryCrashEvidence evidence, CancellationToken token)
    {
        var actual = source.Journal.Submit(evidence.Original, token).Get<CommitReceipt>();
        var expected = new CommitReceipt(D2InventoryCrashProtocol.SelectedId,
            new(D2InventoryCrashCorpus.Source.Owner.Incarnation,
                D2InventoryCrashProtocol.Partition(D2InventoryCrashProtocol.P2).AtomicPartitionId,
                evidence.SelectedIndex, D2InventoryCrashProtocol.InitialRevision),
            [new(D2InventoryCrashProtocol.MutationKind, D2InventoryCrashProtocol.ResourceA,
                D2InventoryCrashProtocol.DocumentId, D2InventoryCrashProtocol.InitialRevision)], DurabilityProfile.ProcessDurable);
        await D2InventoryRecoveryPages.SameAsync(expected, actual);
        return actual;
    }

    internal static async Task ModelAsync(ControlledPartitionMovementNativeNode source, bool selected)
    {
        foreach (var partition in new[] { D2InventoryCrashProtocol.P0, D2InventoryCrashProtocol.P1, D2InventoryCrashProtocol.P2 })
        {
            var document = source.Database.GetDocument(D2InventoryCrashProtocol.Principal,
                new(D2InventoryCrashProtocol.Partition(partition), D2InventoryCrashProtocol.ResourceA, D2InventoryCrashProtocol.DocumentId));
            if (partition == D2InventoryCrashProtocol.P2 && !selected)
            {
                await Assert.That(document).IsNull();
            }
            else
            {
                await Assert.That(document).IsNotNull();
                await Assert.That(document!.Json).IsEqualTo(D2InventoryCrashProtocol.Json);
                await Assert.That(document.Revision).IsEqualTo(D2InventoryCrashProtocol.InitialRevision);
            }
        }
    }

    internal static async Task ReplayAsync(ControlledPartitionMovementNativeNode source,
        D2InventoryCrashEvidence evidence, CommitReceipt expected, CancellationToken token)
    {
        var image = D2InventoryRecoveryPages.Image(source);
        var position = source.Store.Position;
        var index = source.Journal.Log.State.LastIndex;
        var committed = source.Journal.Log.State.CommittedIndex;
        await D2InventoryRecoveryPages.SameAsync(evidence.PrefixReceipt,
            source.Journal.Submit(evidence.PrefixOriginal, token).Get<CommitReceipt>());
        await D2InventoryRecoveryPages.SameAsync(expected, source.Journal.Submit(evidence.Original, token).Get<CommitReceipt>());
        await Assert.That(source.Store.Position).IsEqualTo(position);
        await Assert.That(source.Journal.Log.State.LastIndex).IsEqualTo(index);
        await Assert.That(source.Journal.Log.State.CommittedIndex).IsEqualTo(committed);
        await Assert.That(D2InventoryRecoveryPages.Image(source).SequenceEqual(image, StringComparer.Ordinal)).IsTrue();
    }
}
