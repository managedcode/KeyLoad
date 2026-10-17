using KeyLoad.CrashHost.Features.ResourceExecution;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2ImportAbortBeforePageFlow
{
    internal static async Task ExecuteAsync(D2ImportSession session, bool unknown, CancellationToken token)
    {
        session.Open(token, unknown);
        var aborted = await D2ImportCapture.ExecuteAsync(session, D2ImportCrashProtocol.FirstPartition, token);
        var remaining = await D2ImportCapture.ExecuteAsync(session, D2ImportCrashProtocol.SecondPartition, token);
        // No StagePage or Install is issued for the aborted image, including before the unrelated import starts.
        await D2ImportAbortBeforePageCleanup.RequireAbsentAsync(session, aborted);
        await D2ImportInstall.StageAsync(session, remaining, token);
        await D2ImportAbortPartialCopy.ExecuteAsync(session, remaining, token);
        var other = D2ImportAbortAssertions.Marker(session, remaining);
        await Assert.That(other).IsNotNull();
        List<(ReplicatedOperation Original, OperationResult Result)> writes =
        [
            await WriteAsync(session, D2ImportCrashProtocol.LocalFirst, D2ImportCrashProtocol.FirstJson, unknown, token)
        ];
        await D2ImportInventory.BlockedAsync(session, token);
        var cleanup = await D2ImportAbortBeforePageCleanup.ExecuteAsync(session, aborted, remaining, token);
        await D2ImportReceipts.ReplayAsync(session.Target, [(cleanup.Original, cleanup.Outcome)], token);
        await D2ImportAbortFinalization.ExecuteAsync(session, cleanup.State,
            cleanup.Terminal, cleanup.GrantId, cleanup.Body, token);
        await D2ImportAbortBeforePageCleanup.RequireAbsentAsync(session, aborted);
        await D2ImportAbortAssertions.MarkerAsync(session, remaining, other);
        await VerifyActiveImportAsync(session, aborted, remaining, unknown, token);
        writes.Add(await WriteAsync(session, D2ImportCrashProtocol.LocalSecond, D2ImportCrashProtocol.SecondJson, unknown, token));
        await D2ImportInventory.BlockedAsync(session, token);
        await D2ImportInstall.CompleteAsync(session, remaining, token);
        await D2ImportAbortBeforePageCleanup.RequireAbsentAsync(session, aborted);
        await D2ImportAbortAssertions.MarkerAsync(session, remaining, null);
        await D2ImportAbortAssertions.InventoryAsync(session, unknown, token);
        // Existing cold verification replays every supplied original and independently checks both literal ordinary writes.
        writes.Add((cleanup.Original, cleanup.Outcome));
        await D2ImportAbortCold.ExecuteAsync(session, aborted, remaining, writes, unknown, token);
    }

    private static async Task VerifyActiveImportAsync(D2ImportSession session, D2ImportImage aborted,
        D2ImportImage remaining, bool unknown, CancellationToken token)
    {
        await D2ImportAbortAssertions.OriginAsync(session, unknown);
        await D2ImportAbortAssertions.SourceDocumentsAsync(session);
        await D2ImportAbortAssertions.DocumentAbsentAsync(session, aborted);
        await D2ImportReceipts.ImportedAsync(session, remaining);
        await D2ImportInventory.BlockedAsync(session, token);
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
