namespace KeyLoad.UnitTests.Features.Authorization;

internal static class SafeProjectorOperationInitialFlow
{
    internal static async Task RunAsync(SafeProjectorOperationDatabase owner, SafeProjectorOperationHistory history)
    {
        await history.CurrentAsync(owner, SafeProjectorOperationProtocol.InitialPolicyEpoch, []);
        await ReadsAsync(owner, false, false);
        var initialQuery = await SafeProjectorOperationCursor.CaptureAsync(owner,
            SafeProjectorOperationProtocol.InitialPolicyEpoch, InitialDocumentEpoch,
            SafeProjectorOperationProtocol.InitialSafe, SafeProjectorOperationProtocol.InitialRevision, false, owner.CallerToken);
        var initialFeed = await SafeProjectorOperationChanges.CurrentAsync(owner, history.State,
            SafeProjectorOperationProtocol.InitialPolicyEpoch, false, owner.CallerToken);
        await history.SetAsync(owner, SafeProjectorOperationProtocol.UseOnlyEpoch, [SafeProjectorOperationProtocol.UseGrant]);
        await SafeProjectorOperationCursor.RefuseAsync(owner, initialQuery, initialFeed.Cursor, owner.CallerToken);
        await ReadsAsync(owner, false, true);
        var useQuery = await SafeProjectorOperationCursor.CaptureAsync(owner,
            SafeProjectorOperationProtocol.UseOnlyEpoch, InitialDocumentEpoch,
            SafeProjectorOperationProtocol.InitialSafe, SafeProjectorOperationProtocol.InitialRevision, false, owner.CallerToken);
        var useFeed = await SafeProjectorOperationChanges.CurrentAsync(owner, history.State,
            SafeProjectorOperationProtocol.UseOnlyEpoch, false, owner.CallerToken);
        await history.SetAsync(owner, SafeProjectorOperationProtocol.WithdrawnUseEpoch, []);
        await SafeProjectorOperationCursor.RefuseAsync(owner, useQuery, useFeed.Cursor, owner.CallerToken);
        await ReadsAsync(owner, false, false);
        await SafeProjectorOperationRefusals.RoundtripAsync(owner, history,
            SafeProjectorOperationProtocol.WithdrawnUseEpoch, SafeProjectorOperationProtocol.InitialRevision);
        await SafeProjectorOperationRefusals.ProtectedPatchAsync(owner, history,
            SafeProjectorOperationProtocol.WithdrawnUseEpoch, SafeProjectorOperationProtocol.InitialRevision);
        await history.CurrentAsync(owner, SafeProjectorOperationProtocol.WithdrawnUseEpoch, []);
    }

    private static async Task ReadsAsync(SafeProjectorOperationDatabase owner, bool raw, bool use)
    {
        await SafeProjectorOperationRows.WaveAsync(owner, SafeProjectorOperationProtocol.InitialJson,
            SafeProjectorOperationProtocol.InitialSafe, SafeProjectorOperationProtocol.InitialStatus,
            SafeProjectorOperationProtocol.InitialRevision, raw, owner.CallerToken);
        await SafeProjectorOperationAuthority.UsageAsync(owner, use,
            raw ? SafeProjectorOperationProtocol.InitialJson : SafeProjectorOperationProtocol.InitialSafe,
            SafeProjectorOperationProtocol.InitialRevision, raw, owner.CallerToken);
    }

    private const long InitialDocumentEpoch = 1;
}
