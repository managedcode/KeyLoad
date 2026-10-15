using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class DocumentLiveProfileRf3Policy
{
    internal static async Task RunAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        DocumentLiveProfileRf3State state, CancellationToken token)
    {
        await DocumentLiveProfileRf3Setup.PrincipalAsync(root, state, 2L, false, false, token);
        await DocumentLiveProfileRf3Values.ObserveAsync(root, state, 1, () => DocumentLiveProfileRf3Refusals.BothAsync(reader, state,
            state.Query, state.OriginalCursor, ErrorCode.PermissionDenied, "The principal cannot perform this operation in this scope.", token), token);
        await DocumentLiveProfileRf3Setup.PrincipalAsync(root, state, 3L, true, true, token);
        await DocumentLiveProfileRf3Values.ObserveAsync(root, state, 1, () => DocumentLiveProfileRf3Refusals.UnauthenticatedAsync(reader, state, token), token);
        await DocumentLiveProfileRf3Setup.PrincipalAsync(root, state, 4L, true, false, token);
        await DocumentLiveProfileRf3Values.ObserveAsync(root, state, 1, async () =>
        {
            await DocumentLiveProfileRf3Refusals.ReadAsync(reader, state, new(state.Query, state.OriginalCursor),
                ErrorCode.TokenInvalidated, DocumentLiveProfileRf3State.CursorInvalidated, token);
            state.FreshCursor = await DocumentLiveProfileRf3Pages.SnapshotAsync(reader, state, 1, token);
        }, token);
    }
}
