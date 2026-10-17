using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.ResourceExecution;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2ImportRecoveryFlow
{
    internal static async Task ExecuteAsync(D2ImportSession session, D2ImportProcess process,
        D2InventoryCrashCut cut, CancellationToken token)
    {
        session.Open(token);
        var first = await D2ImportCapture.ExecuteAsync(session, D2ImportCrashProtocol.FirstPartition, token);
        var second = await D2ImportCapture.ExecuteAsync(session, D2ImportCrashProtocol.SecondPartition, token);
        await D2ImportInstall.StageAsync(session, first, token);
        await D2ImportInstall.StageAsync(session, second, token);
        await D2ImportInstall.OneAsync(session, first, token);
        await D2ImportInstall.OneAsync(session, second, token);
        await ActiveAsync(session, first, second, token);
        var original = D2ImportSeed.Write(session.Target,
            D2ImportCrashProtocol.LocalPartition(D2ImportCrashProtocol.LocalFirst),
            D2ImportCrashProtocol.LocalCollection, D2ImportCrashProtocol.FirstJson);
        var input = new D2ImportCrashInput(session.Owners.Destination.Owner, original,
            session.Target.Store.Position, session.Target.Journal.Log.State.LastIndex);
        await ControlledPartitionMovementProcessFiles.WriteAsync(session.Root,
            D2ImportCrashProtocol.InputFile, input, token);
        session.CloseTarget();
        await process.ExecuteAsync(cut, token);
        session.ReopenTarget();
        var selected = await D2ImportRecovery.SelectedAsync(session, input, cut, token);
        await ActiveAsync(session, first, second, token);
        await D2ImportInstall.CompleteAsync(session, first, token);
        await D2ImportInventory.CheckpointAsync(session, first, present: false);
        await D2ImportInventory.CheckpointAsync(session, second, present: true);
        await D2ImportInventory.BlockedAsync(session, token);
        await D2ImportReceipts.ImportedAsync(session, first);
        var next = D2ImportSeed.Write(session.Target,
            D2ImportCrashProtocol.LocalPartition(D2ImportCrashProtocol.LocalSecond),
            D2ImportCrashProtocol.LocalCollection, D2ImportCrashProtocol.SecondJson);
        var nextIndex = checked(session.Target.Journal.Log.State.LastIndex + D2ImportCrashProtocol.Single);
        _ = await D2ImportReceipts.WriteAsync(session, next, D2ImportCrashProtocol.LocalSecond, nextIndex, token);
        var nextResult = session.Target.Journal.Submit(next, token);
        await D2ImportReceipts.LocalDocumentAsync(session, next,
            D2ImportCrashProtocol.LocalSecond, D2ImportCrashProtocol.SecondJson);
        await D2ImportInventory.CheckpointAsync(session, second, present: true);
        await D2ImportInventory.BlockedAsync(session, token);
        await D2ImportInstall.CompleteAsync(session, second, token);
        await D2ImportInventory.CheckpointAsync(session, first, present: false);
        await D2ImportInventory.CheckpointAsync(session, second, present: false);
        await D2ImportInventory.CompleteAsync(session, token);
        await D2ImportReceipts.ImportedAsync(session, second);
        await D2ImportRecovery.ColdAsync(session, first, second, [(original, selected), (next, nextResult)], token);
    }

    private static async Task ActiveAsync(D2ImportSession session, D2ImportImage first,
        D2ImportImage second, CancellationToken token)
    {
        await D2ImportInventory.CheckpointAsync(session, first, present: true);
        await D2ImportInventory.CheckpointAsync(session, second, present: true);
        await D2ImportInventory.BlockedAsync(session, token);
    }
}
