using KeyLoad.ClusterTests.OrleansExecution;

namespace KeyLoad.ClusterTests.Authorization;

internal static class SafeProjectorOperationClusterInitial
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, CancellationToken token)
    {
        await WaveAsync(root, reader, history, false, false, token);
        var feed = await SafeProjectorOperationClusterChanges.WaveAsync(root, reader, history, false, token);
        var initial = await SafeProjectorOperationClusterCursor.CaptureAsync(root, reader, history, false, feed.Cursor, token);
        await SafeProjectorOperationClusterAuthority.SetAsync(root, history, SafeProjectorOperationClusterProtocol.UseOnlyEpoch,
            [SafeProjectorOperationClusterProtocol.UseGrant], token);
        await SafeProjectorOperationClusterCursor.RefuseAsync(root, reader, history, initial, token);
        await WaveAsync(root, reader, history, false, true, token);
        feed = await SafeProjectorOperationClusterChanges.WaveAsync(root, reader, history, false, token);
        var useOnly = await SafeProjectorOperationClusterCursor.CaptureAsync(root, reader, history, false, feed.Cursor, token);
        await SafeProjectorOperationClusterAuthority.SetAsync(root, history, SafeProjectorOperationClusterProtocol.WithdrawnUseEpoch, [], token);
        await SafeProjectorOperationClusterCursor.RefuseAsync(root, reader, history, useOnly, token);
        await WaveAsync(root, reader, history, false, false, token);
        await SafeProjectorOperationClusterMutations.RoundtripAsync(root, reader, history,
            SafeProjectorOperationClusterProtocol.WithdrawnUseEpoch, token);
        await SafeProjectorOperationClusterMutations.ProtectedRefusedAsync(root, reader, history,
            SafeProjectorOperationClusterProtocol.WithdrawnUseEpoch, token);
        await SafeProjectorOperationClusterChanges.WaveAsync(root, reader, history, false, token);
    }

    internal static async Task WaveAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, bool raw, bool use, CancellationToken token)
    {
        await SafeProjectorOperationClusterRows.WaveAsync(root, reader, history, raw, token);
        await SafeProjectorOperationClusterSelectionAssertions.WaveAsync(root, reader, history, raw, token);
        await SafeProjectorOperationClusterAuthority.UsageAsync(root, reader, history, use, raw, token);
    }
}
