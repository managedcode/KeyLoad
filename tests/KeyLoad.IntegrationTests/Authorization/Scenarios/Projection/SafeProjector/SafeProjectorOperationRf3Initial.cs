using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationRf3Initial
{
    internal static async Task RunAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, CancellationToken token)
    {
        await WaveAsync(root, reader, history, false, false, token);
        var feed = await SafeProjectorOperationRf3Changes.WaveAsync(root, reader, history, false, token);
        var initial = await SafeProjectorOperationRf3Cursor.CaptureAsync(root, reader, history, false, feed.Cursor, token);
        await SafeProjectorOperationRf3Authority.SetAsync(root, history, SafeProjectorOperationRf3Protocol.UseOnlyEpoch,
            [SafeProjectorOperationRf3Protocol.UseGrant], token);
        await SafeProjectorOperationRf3Cursor.RefuseAsync(root, reader, history, initial, token);
        await WaveAsync(root, reader, history, false, true, token);
        feed = await SafeProjectorOperationRf3Changes.WaveAsync(root, reader, history, false, token);
        var useOnly = await SafeProjectorOperationRf3Cursor.CaptureAsync(root, reader, history, false, feed.Cursor, token);
        await SafeProjectorOperationRf3Authority.SetAsync(root, history, SafeProjectorOperationRf3Protocol.WithdrawnUseEpoch, [], token);
        await SafeProjectorOperationRf3Cursor.RefuseAsync(root, reader, history, useOnly, token);
        await WaveAsync(root, reader, history, false, false, token);
        await SafeProjectorOperationRf3Mutations.RoundtripAsync(root, reader, history,
            SafeProjectorOperationRf3Protocol.WithdrawnUseEpoch, token);
        await SafeProjectorOperationRf3Mutations.ProtectedRefusedAsync(root, reader, history,
            SafeProjectorOperationRf3Protocol.WithdrawnUseEpoch, token);
        await SafeProjectorOperationRf3Changes.WaveAsync(root, reader, history, false, token);
    }

    internal static async Task WaveAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, bool raw, bool use, CancellationToken token)
    {
        await SafeProjectorOperationRf3Rows.WaveAsync(root, reader, history, raw, token);
        await SafeProjectorOperationRf3SelectionAssertions.WaveAsync(root, reader, history, raw, token);
        await SafeProjectorOperationRf3Authority.UsageAsync(root, reader, history, use, raw, token);
    }
}
