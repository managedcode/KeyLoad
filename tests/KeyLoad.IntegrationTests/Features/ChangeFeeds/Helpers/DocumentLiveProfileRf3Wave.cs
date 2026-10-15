using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class DocumentLiveProfileRf3Wave
{
    internal static async Task WarmAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        DocumentLiveProfileRf3State state, ModelQuerySourceKind kind, CancellationToken token)
    {
        await DocumentLiveProfileRf3Values.ObserveAsync(root, state, 0, async () =>
        {
            state.OriginalCursor = await DocumentLiveProfileRf3Pages.SnapshotAsync(reader, state, 0, token);
            await DocumentLiveProfileRf3Refusals.BothAsync(reader, state, state.Model(kind), state.OriginalCursor,
                ErrorCode.UnsupportedCapability, DocumentLiveProfileRf3State.Unsupported, token);
            await DocumentLiveProfileRf3Refusals.BothAsync(reader, state, state.Model(kind) with
            {
                AllowFullScan = false
            }, state.OriginalCursor,
                ErrorCode.UnsupportedCapability, "A model query requires explicit full-scan consent.", token);
            await DocumentLiveProfileRf3Calls.CancelledAsync(reader, state, token);
        }, token);
        await DocumentLiveProfileRf3Setup.UpdateAsync(root, state, token);
        await DocumentLiveProfileRf3Values.ObserveAsync(root, state, 1, async () =>
        {
            await DocumentLiveProfileRf3Refusals.ReadAsync(reader, state, new(state.Query, state.OriginalCursor, MaxBytes: 1),
                ErrorCode.BudgetExceeded, "The first change exceeds the page byte budget.", token);
            await DocumentLiveProfileRf3Pages.TailAsync(reader, state, token);
        }, token);
        await DocumentLiveProfileRf3Policy.RunAsync(root, reader, state, token);
        await DocumentLiveProfileRf3Setup.FreshAsync(root, state, token);
        await FinalAsync(root, reader, state, kind, token);
    }
    internal static Task FinalAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        DocumentLiveProfileRf3State state, ModelQuerySourceKind kind, CancellationToken token)
        => DocumentLiveProfileRf3Values.ObserveAsync(root, state, 2, async () =>
        {
            await DocumentLiveProfileRf3Refusals.BothAsync(reader, state, state.Model(kind), state.FreshCursor,
                ErrorCode.UnsupportedCapability, DocumentLiveProfileRf3State.Unsupported, token);
            await DocumentLiveProfileRf3Refusals.ReadAsync(reader, state, new(state.Query, state.OriginalCursor),
                ErrorCode.TokenInvalidated, DocumentLiveProfileRf3State.CursorInvalidated, token);
            await DocumentLiveProfileRf3Pages.SnapshotAsync(reader, state, 2, token);
            await DocumentLiveProfileRf3Pages.FreshAsync(reader, state, token);
            await DocumentLiveProfileRf3Values.ReplayAsync(root, state, token);
        }, token);
}
