using KeyLoad.IntegrationTests.Features.OrleansExecution;

namespace KeyLoad.IntegrationTests.Features.ChangeFeeds;

internal static class DocumentLiveProfileClusterPolicy
{
    internal static async Task RunAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        DocumentLiveProfileClusterState state, CancellationToken token)
    {
        await DocumentLiveProfileClusterSetup.PrincipalAsync(root, state, 2L, false, false, token);
        await DocumentLiveProfileClusterValues.ObserveAsync(root, state, 1, () => DocumentLiveProfileClusterRefusals.BothAsync(reader, state,
            state.Query, state.OriginalCursor, ErrorCode.PermissionDenied, "The principal cannot perform this operation in this scope.", token), token);
        await DocumentLiveProfileClusterSetup.PrincipalAsync(root, state, 3L, true, true, token);
        await DocumentLiveProfileClusterValues.ObserveAsync(root, state, 1, () => DocumentLiveProfileClusterRefusals.UnauthenticatedAsync(reader, state, token), token);
        await DocumentLiveProfileClusterSetup.PrincipalAsync(root, state, 4L, true, false, token);
        await DocumentLiveProfileClusterValues.ObserveAsync(root, state, 1, async () =>
        {
            await DocumentLiveProfileClusterRefusals.ReadAsync(reader, state, new(state.Query, state.OriginalCursor),
                ErrorCode.TokenInvalidated, DocumentLiveProfileClusterState.CursorInvalidated, token);
            state.FreshCursor = await DocumentLiveProfileClusterPages.SnapshotAsync(reader, state, 1, token);
        }, token);
    }
}
