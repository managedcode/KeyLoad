using KeyLoad.CrashHost.Features.ResourceExecution;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2ImportAbortFlow
{
    internal static async Task ExecuteAsync(D2ImportSession session, bool unknown, CancellationToken token)
    {
        session.Open(token, unknown);
        await D2ImportAbortAssertions.OriginAsync(session, unknown);
        var aborted = await D2ImportCapture.ExecuteAsync(session, D2ImportCrashProtocol.FirstPartition, token);
        var remaining = await D2ImportCapture.ExecuteAsync(session, D2ImportCrashProtocol.SecondPartition, token);
        await D2ImportInstall.StageAsync(session, aborted, token);
        await D2ImportInstall.StageAsync(session, remaining, token);
        await D2ImportAbortPartialCopy.ExecuteAsync(session, aborted, token);
        await D2ImportAbortPartialCopy.ExecuteAsync(session, remaining, token);
        await D2ImportAbortAssertions.OriginAsync(session, unknown);
        List<(ReplicatedOperation Original, OperationResult Result)> writes =
        [
            await WriteAsync(session, D2ImportCrashProtocol.LocalFirst, D2ImportCrashProtocol.FirstJson, unknown, token)
        ];
        await D2ImportInventory.BlockedAsync(session, token);
        var cleanup = await D2ImportAbortCleanup.ExecuteAsync(session, aborted, remaining, unknown, token);
        await D2ImportAbortAssertions.OriginAsync(session, unknown);
        await D2ImportAbortFinalization.ExecuteAsync(session, cleanup.State,
            cleanup.Terminal, cleanup.GrantId, cleanup.Body, token);
        await D2ImportAbortAssertions.SourceDocumentsAsync(session);
        await D2ImportReceipts.ImportedAsync(session, remaining);
        writes.Add(await WriteAsync(session, D2ImportCrashProtocol.LocalSecond, D2ImportCrashProtocol.SecondJson, unknown, token));
        await D2ImportInventory.BlockedAsync(session, token);
        await D2ImportInstall.CompleteAsync(session, remaining, token);
        await D2ImportAbortAssertions.MarkerAsync(session, aborted, null);
        await D2ImportAbortAssertions.MarkerAsync(session, remaining, null);
        await D2ImportAbortAssertions.DocumentAbsentAsync(session, aborted);
        await D2ImportReceipts.ImportedAsync(session, remaining);
        await D2ImportAbortAssertions.InventoryAsync(session, unknown, token);
        writes.Add((cleanup.Original, cleanup.Outcome));
        await D2ImportAbortCold.ExecuteAsync(session, aborted, remaining, writes, unknown, token);
    }

    private static async Task<(ReplicatedOperation Original, OperationResult Result)> WriteAsync(
        D2ImportSession session, string partition, string json, bool unknown, CancellationToken token)
    {
        var original = D2ImportSeed.Write(session.Target, D2ImportCrashProtocol.LocalPartition(partition),
            D2ImportCrashProtocol.LocalCollection, json);
        var index = checked(session.Target.Journal.Log.State.LastIndex + D2ImportCrashProtocol.Single);
        var receipt = await D2ImportReceipts.WriteAsync(session, original, partition, index, token);
        var actual = session.Target.Journal.Submit(original, token);
        await D2InventoryRecoveryPages.SameAsync(receipt, actual.Get<CommitReceipt>());
        await D2ImportReceipts.LocalDocumentAsync(session, original, partition, json);
        await D2ImportAbortAssertions.OriginAsync(session, unknown);
        return (original, actual);
    }
}
