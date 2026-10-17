using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.ResourceExecution;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2ImportRecovery
{
    internal static async Task<OperationResult> SelectedAsync(D2ImportSession session,
        D2ImportCrashInput input, D2InventoryCrashCut cut, CancellationToken token)
    {
        var committed = cut != D2InventoryCrashCut.EntryAcknowledged;
        await Assert.That(session.Target.Journal.Log.State.LastIndex)
            .IsEqualTo(input.BeforeIndex + D2ImportCrashProtocol.Single);
        await Assert.That(session.Target.Journal.Log.State.CommittedIndex)
            .IsEqualTo(input.BeforeIndex + (committed ? D2ImportCrashProtocol.Single : D2ImportCrashProtocol.Empty));
        if (!committed)
        {
            await Assert.That(session.Target.Database.GetDocument(D2ImportCrashProtocol.Principal,
                new(D2ImportCrashProtocol.LocalPartition(D2ImportCrashProtocol.LocalFirst),
                    D2ImportCrashProtocol.LocalCollection, D2ImportCrashProtocol.DocumentId), cancellationToken: token)).IsNull();
            await Assert.That(session.Target.Store.Position).IsEqualTo(input.BeforePosition);
        }
        var receipt = await D2ImportReceipts.WriteAsync(session, input.Original,
            D2ImportCrashProtocol.LocalFirst, input.BeforeIndex + D2ImportCrashProtocol.Single, token);
        var outcome = session.Target.Journal.Submit(input.Original, token);
        await D2InventoryRecoveryPages.SameAsync(receipt, outcome.Get<CommitReceipt>());
        if (cut == D2InventoryCrashCut.AppliedAcknowledged)
        {
            var acknowledged = await ControlledPartitionMovementProcessFiles.ReadAsync<OperationResult>(
                session.Root, D2ImportCrashProtocol.ResultFile, token);
            await D2InventoryRecoveryPages.SameAsync(acknowledged, outcome);
        }
        await D2ImportReceipts.LocalDocumentAsync(session, input.Original,
            D2ImportCrashProtocol.LocalFirst, D2ImportCrashProtocol.FirstJson);
        return outcome;
    }

    internal static async Task ColdAsync(D2ImportSession session, D2ImportImage first, D2ImportImage second,
        IReadOnlyList<(ReplicatedOperation Original, OperationResult Result)> writes, CancellationToken token)
    {
        var sourceRows = D2InventoryRecoveryPages.Image(session.Source);
        var targetRows = D2InventoryRecoveryPages.Image(session.Target);
        var sourceIdentity = session.Source.Store.Identity;
        var targetIdentity = session.Target.Store.Identity;
        var sourcePosition = session.Source.Store.Position;
        var targetPosition = session.Target.Store.Position;
        var sourceState = session.Source.Journal.Log.State;
        var targetState = session.Target.Journal.Log.State;
        for (var cycle = D2ImportCrashProtocol.Empty; cycle < D2ImportCrashProtocol.Pair; cycle++)
        {
            session.Cold();
            await D2ImportReceipts.ReplayAsync(session.Source, session.SourceSeeds, token);
            await D2ImportReceipts.ReplayAsync(session.Target, writes.Concat(first.Installs).Concat(second.Installs), token);
            await D2ImportInventory.CompleteAsync(session, token);
            await D2ImportInventory.CheckpointAsync(session, first, present: false);
            await D2ImportInventory.CheckpointAsync(session, second, present: false);
            await D2ImportReceipts.ImportedAsync(session, first);
            await D2ImportReceipts.ImportedAsync(session, second);
            await D2ImportReceipts.LocalDocumentAsync(session, writes[D2ImportCrashProtocol.Empty].Original,
                D2ImportCrashProtocol.LocalFirst, D2ImportCrashProtocol.FirstJson);
            await D2ImportReceipts.LocalDocumentAsync(session, writes[D2ImportCrashProtocol.Single].Original,
                D2ImportCrashProtocol.LocalSecond, D2ImportCrashProtocol.SecondJson);
            await D2InventoryRecoveryPages.SameAsync(sourceIdentity, session.Source.Store.Identity);
            await D2InventoryRecoveryPages.SameAsync(targetIdentity, session.Target.Store.Identity);
            await Assert.That(session.Source.Store.Position).IsEqualTo(sourcePosition);
            await Assert.That(session.Target.Store.Position).IsEqualTo(targetPosition);
            await D2InventoryRecoveryPages.SameAsync(sourceState, session.Source.Journal.Log.State);
            await D2InventoryRecoveryPages.SameAsync(targetState, session.Target.Journal.Log.State);
            await Assert.That(D2InventoryRecoveryPages.Image(session.Source).SequenceEqual(sourceRows, StringComparer.Ordinal)).IsTrue();
            await Assert.That(D2InventoryRecoveryPages.Image(session.Target).SequenceEqual(targetRows, StringComparer.Ordinal)).IsTrue();
        }
    }
}
