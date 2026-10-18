using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class DocumentLiveProfileClusterWave
{
    internal static async Task WarmAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        DocumentLiveProfileClusterState state, ModelQuerySourceKind kind, CancellationToken token)
    {
        await DocumentLiveProfileClusterValues.ObserveAsync(root, state, 0, async () =>
        {
            state.OriginalCursor = await DocumentLiveProfileClusterPages.SnapshotAsync(reader, state, 0, token);
            await DocumentLiveProfileClusterRefusals.BothAsync(reader, state, state.Model(kind), state.OriginalCursor,
                ErrorCode.UnsupportedCapability, DocumentLiveProfileClusterState.Unsupported, token);
            await DocumentLiveProfileClusterRefusals.BothAsync(reader, state, state.Model(kind) with
            {
                AllowFullScan = false
            }, state.OriginalCursor,
                ErrorCode.UnsupportedCapability, "A model query requires explicit full-scan consent.", token);
            await DocumentLiveProfileClusterCalls.CancelledAsync(reader, state, token);
        }, token);
        await DocumentLiveProfileClusterSetup.UpdateAsync(root, state, token);
        await DocumentLiveProfileClusterValues.ObserveAsync(root, state, 1, async () =>
        {
            await DocumentLiveProfileClusterRefusals.ReadAsync(reader, state, new(state.Query, state.OriginalCursor, MaxBytes: 1),
                ErrorCode.BudgetExceeded, "The first change exceeds the page byte budget.", token);
            await DocumentLiveProfileClusterPages.TailAsync(reader, state, token);
        }, token);
        await DocumentLiveProfileClusterPolicy.RunAsync(root, reader, state, token);
        await DocumentLiveProfileClusterSetup.FreshAsync(root, state, token);
        await FinalAsync(root, reader, state, kind, token);
    }
    internal static Task FinalAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        DocumentLiveProfileClusterState state, ModelQuerySourceKind kind, CancellationToken token)
        => DocumentLiveProfileClusterValues.ObserveAsync(root, state, 2, async () =>
        {
            await DocumentLiveProfileClusterRefusals.BothAsync(reader, state, state.Model(kind), state.FreshCursor,
                ErrorCode.UnsupportedCapability, DocumentLiveProfileClusterState.Unsupported, token);
            await DocumentLiveProfileClusterRefusals.ReadAsync(reader, state, new(state.Query, state.OriginalCursor),
                ErrorCode.TokenInvalidated, DocumentLiveProfileClusterState.CursorInvalidated, token);
            await DocumentLiveProfileClusterPages.SnapshotAsync(reader, state, 2, token);
            await DocumentLiveProfileClusterPages.FreshAsync(reader, state, token);
            await DocumentLiveProfileClusterValues.ReplayAsync(root, state, token);
        }, token);
}
