using KeyLoad.CrashHost.Features.ResourceExecution;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2InventoryRecoveryFlow
{
    private const int ColdCycles = 2;

    internal static async Task VerifyAsync(string root, D2InventoryCrashCut cut, CancellationToken token)
    {
        var evidence = NativeSerialization.Deserialize<D2InventoryCrashEvidence>(
            await File.ReadAllBytesAsync(Path.Combine(root, D2InventoryCrashProtocol.EvidenceFile), token));
        CommitReceipt? expected = null;
        string[]? sourceImage = null;
        string[]? targetImage = null;
        Guid sourceNodeId = default;
        Guid targetNodeId = default;
        await D2InventoryRecoveryOwners.WithAsync(root, async (source, target) =>
        {
            _ = source.Journal.Submit(evidence.BeforeSelectedOperation, token).Get<ResourceDefinition>();
            await D2InventoryRecoveryPages.SameAsync(evidence.PrefixReceipt,
                source.Journal.Submit(evidence.PrefixOriginal, token).Get<CommitReceipt>());
            var committed = cut != D2InventoryCrashCut.EntryAcknowledged;
            await Assert.That(source.Journal.Log.State.CommittedIndex).IsEqualTo(
                committed ? evidence.SelectedIndex : evidence.SelectedIndex - D2InventoryCrashProtocol.IndexStep);
            if (committed)
            {
                expected = await D2InventoryRecoveryReceipts.RequireSelectedAsync(source, evidence, token);
            }
            await D2InventoryRecoveryReceipts.ModelAsync(source, committed);
            await D2InventoryRecoveryPages.RequireAsync(source, target, committed, token);
            if (!committed)
            {
                await Assert.That(source.Store.Position).IsEqualTo(evidence.PrefixPosition);
                expected = await D2InventoryRecoveryReceipts.RequireSelectedAsync(source, evidence, token);
            }
            if (cut == D2InventoryCrashCut.AppliedAcknowledged)
            {
                var receipt = NativeSerialization.Deserialize<CommitReceipt>(
                    await File.ReadAllBytesAsync(Path.Combine(root, D2InventoryCrashProtocol.ReceiptFile), token));
                await D2InventoryRecoveryPages.SameAsync(receipt, expected);
            }
            await D2InventoryRecoveryPages.RequireAsync(source, target, selectedCommitted: true, token);
            await D2InventoryRecoveryReceipts.ModelAsync(source, selected: true);
            await D2InventoryRecoveryReceipts.ReplayAsync(source, evidence, expected!, token);
            await D2InventoryRecoveryRefusals.CheckAsync(source, token);
            sourceImage = D2InventoryRecoveryPages.Image(source);
            targetImage = D2InventoryRecoveryPages.Image(target);
            sourceNodeId = source.Store.Identity.NodeId;
            targetNodeId = target.Store.Identity.NodeId;
        });
        for (var pass = 0; pass < ColdCycles; pass++)
        {
            await D2InventoryRecoveryOwners.WithAsync(root, async (source, target) =>
            {
                await D2InventoryRecoveryReceipts.ReplayAsync(source, evidence, expected!, token);
                await D2InventoryRecoveryPages.RequireAsync(source, target, selectedCommitted: true, token);
                await D2InventoryRecoveryReceipts.ModelAsync(source, selected: true);
                await Assert.That(source.Store.Identity.NodeId).IsEqualTo(sourceNodeId);
                await Assert.That(target.Store.Identity.NodeId).IsEqualTo(targetNodeId);
                await Assert.That(D2InventoryRecoveryPages.Image(source).SequenceEqual(sourceImage!, StringComparer.Ordinal)).IsTrue();
                await Assert.That(D2InventoryRecoveryPages.Image(target).SequenceEqual(targetImage!, StringComparer.Ordinal)).IsTrue();
            });
        }
    }
}
