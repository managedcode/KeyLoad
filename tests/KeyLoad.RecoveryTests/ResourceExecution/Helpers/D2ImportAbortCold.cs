using KeyLoad.CrashHost.Features.ResourceExecution;

namespace KeyLoad.RecoveryTests.Features.ResourceExecution;

internal static class D2ImportAbortCold
{
    internal static async Task ExecuteAsync(D2ImportSession session, D2ImportImage aborted, D2ImportImage remaining,
        IReadOnlyList<(ReplicatedOperation Original, OperationResult Result)> writes, bool unknown, CancellationToken token)
    {
        var sourceRows = D2InventoryRecoveryPages.Image(session.Source);
        var targetRows = D2InventoryRecoveryPages.Image(session.Target);
        var sourceIdentity = session.Source.Store.Identity;
        var targetIdentity = session.Target.Store.Identity;
        var sourcePosition = session.Source.Store.Position;
        var targetPosition = session.Target.Store.Position;
        var sourceState = session.Source.Journal.Log.State;
        var targetState = session.Target.Journal.Log.State;
        await VerifyAsync(session, aborted, remaining, writes, unknown, token);
        for (var cycle = D2ImportCrashProtocol.Empty; cycle < D2ImportCrashProtocol.Pair; cycle++)
        {
            session.Cold();
            await VerifyAsync(session, aborted, remaining, writes, unknown, token);
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

    private static async Task VerifyAsync(D2ImportSession session, D2ImportImage aborted, D2ImportImage remaining,
        IReadOnlyList<(ReplicatedOperation Original, OperationResult Result)> writes, bool unknown, CancellationToken token)
    {
        await D2ImportReceipts.ReplayAsync(session.Source, session.SourceSeeds, token);
        await D2ImportReceipts.ReplayAsync(session.Target, writes, token);
        await D2ImportAbortAssertions.MarkerAsync(session, aborted, null);
        await D2ImportAbortAssertions.MarkerAsync(session, remaining, null);
        await D2ImportAbortAssertions.SourceDocumentsAsync(session);
        await D2ImportAbortAssertions.DocumentAbsentAsync(session, aborted);
        await D2ImportReceipts.ImportedAsync(session, remaining);
        await D2ImportAbortAssertions.InventoryAsync(session, unknown, token);
        await D2ImportReceipts.LocalDocumentAsync(session, writes[D2ImportCrashProtocol.Empty].Original,
            D2ImportCrashProtocol.LocalFirst, D2ImportCrashProtocol.FirstJson);
        await D2ImportReceipts.LocalDocumentAsync(session, writes[D2ImportCrashProtocol.Single].Original,
            D2ImportCrashProtocol.LocalSecond, D2ImportCrashProtocol.SecondJson);
    }
}
