using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.OrleansExecution;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationClusterCursor
{
    private const string QueryPrefix = "SELECT TOP (1) * FROM [";
    private const string QuerySuffix = "] d ORDER BY d.id";
    private const string FullScan = "bounded-full-scan";
    private const string MissingCursor = "The actual bounded public safe projector query cursor is absent.";
    private const string InvalidQuery = "The query cursor no longer has a valid authorized read cut.";
    private const string InvalidFeed = "The change-feed cursor requires a fresh authorized snapshot.";

    internal static async Task<SafeProjectorOperationClusterCursors> CaptureAsync(RequestCqrsClusterCallers root,
        RequestCqrsClusterCallers reader, SafeProjectorOperationClusterHistory history, bool raw, string feed, CancellationToken token)
    {
        string? sdkCursor = null;
        string? mcpCursor = null;
        foreach (var official in new[] { false, true })
        {
            await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
            {
                var request = Request(history.State);
                var page = official ? (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(
                    McpCallerTools.QueryExecute, request, token))).Value
                    : await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAsync(request, token));
                await Assert.That(page.CutPosition).IsGreaterThan(SafeProjectorOperationClusterProtocol.PositivePublicPosition);
                var cursor = page.Cursor ?? throw new InvalidOperationException(MissingCursor);
                await SqlClusterProtocol.EqualAsync(new QueryPage(
                    [new(SafeProjectorOperationClusterProtocol.First, history.Revision, raw ? history.RawJson : history.SafeJson,
                        !raw, raw ? [] : SafeProjectorOperationClusterProtocol.RedactedPaths)], cursor, page.CutPosition, FullScan), page);
                if (official)
                {
                    mcpCursor = cursor;
                }
                else
                {
                    sdkCursor = cursor;
                }
            }, token);
        }
        var result = new SafeProjectorOperationClusterCursors(sdkCursor ?? throw new InvalidOperationException(MissingCursor),
            mcpCursor ?? throw new InvalidOperationException(MissingCursor), feed);
        await FollowingAsync(root, reader, history, result, raw, token);
        return result;
    }

    internal static async Task RefuseAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, SafeProjectorOperationClusterCursors cursors, CancellationToken token)
    {
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            await SafeProjectorOperationClusterErrors.SdkAsync(await reader.Sdk.QueryAsync(Request(history.State, cursors.SdkQuery), token),
                ErrorCode.CursorExpired, InvalidQuery);
        }, token);
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            await SafeProjectorOperationClusterErrors.McpAsync(await reader.Mcp.CallAsync(McpCallerTools.QueryExecute,
                Request(history.State, cursors.McpQuery), token), ErrorCode.CursorExpired, history.State);
        }, token);
        var feedRequest = new ReadChangeFeedRequest(history.State.Partition, history.State.Collection, Cursor: cursors.Feed);
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            await SafeProjectorOperationClusterErrors.SdkAsync(await reader.Sdk.ReadChangesAsync(feedRequest, token),
                ErrorCode.TokenInvalidated, InvalidFeed);
        }, token);
        await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
        {
            await SafeProjectorOperationClusterErrors.McpAsync(await reader.Mcp.CallAsync(McpCallerTools.ChangesRead,
                feedRequest, token), ErrorCode.TokenInvalidated, history.State);
        }, token);
    }

    internal static async Task FollowingAsync(RequestCqrsClusterCallers root, RequestCqrsClusterCallers reader,
        SafeProjectorOperationClusterHistory history, SafeProjectorOperationClusterCursors cursors, bool raw, CancellationToken token)
    {
        foreach (var official in new[] { false, true })
        {
            await SafeProjectorOperationClusterImages.ObserveAsync(root, history, async () =>
            {
                var request = Request(history.State, official ? cursors.McpQuery : cursors.SdkQuery);
                var actual = official ? (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(
                    McpCallerTools.QueryExecute, request, token))).Value
                    : await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAsync(request, token));
                await Assert.That(actual.CutPosition).IsGreaterThan(SafeProjectorOperationClusterProtocol.PositivePublicPosition);
                await SqlClusterProtocol.EqualAsync(new QueryPage(
                    [new(SafeProjectorOperationClusterProtocol.Second, SafeProjectorOperationClusterProtocol.InitialRevision,
                        SafeProjectorOperationClusterProtocol.SecondJson, !raw, raw ? [] : SafeProjectorOperationClusterProtocol.RedactedPaths)],
                    null, actual.CutPosition, FullScan), actual);
            }, token);
        }
    }

    private static QueryRequest Request(SafeProjectorOperationClusterState state, string? cursor = null)
        => new(state.Partition, QueryPrefix + state.Collection + QuerySuffix, AllowFullScan: true, Cursor: cursor);
}
