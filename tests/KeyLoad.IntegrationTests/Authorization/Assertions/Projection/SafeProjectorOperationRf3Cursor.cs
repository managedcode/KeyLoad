using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class SafeProjectorOperationRf3Cursor
{
    private const string QueryPrefix = "SELECT TOP (1) * FROM [";
    private const string QuerySuffix = "] d ORDER BY d.id";
    private const string FullScan = "bounded-full-scan";
    private const string MissingCursor = "The actual bounded public safe projector query cursor is absent.";
    private const string InvalidQuery = "The query cursor no longer has a valid authorized read cut.";
    private const string InvalidFeed = "The change-feed cursor requires a fresh authorized snapshot.";

    internal static async Task<SafeProjectorOperationRf3Cursors> CaptureAsync(RequestCqrsRf3Callers root,
        RequestCqrsRf3Callers reader, SafeProjectorOperationRf3History history, bool raw, string feed, CancellationToken token)
    {
        string? sdkCursor = null;
        string? mcpCursor = null;
        foreach (var official in new[] { false, true })
        {
            await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
            {
                var request = Request(history.State);
                var page = official ? (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(
                    McpCallerTools.QueryExecute, request, token))).Value
                    : await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAsync(request, token));
                await Assert.That(page.CutPosition).IsGreaterThan(SafeProjectorOperationRf3Protocol.PositivePublicPosition);
                var cursor = page.Cursor ?? throw new InvalidOperationException(MissingCursor);
                await SqlRf3Protocol.EqualAsync(new QueryPage(
                    [new(SafeProjectorOperationRf3Protocol.First, history.Revision, raw ? history.RawJson : history.SafeJson,
                        !raw, raw ? [] : SafeProjectorOperationRf3Protocol.RedactedPaths)], cursor, page.CutPosition, FullScan), page);
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
        var result = new SafeProjectorOperationRf3Cursors(sdkCursor ?? throw new InvalidOperationException(MissingCursor),
            mcpCursor ?? throw new InvalidOperationException(MissingCursor), feed);
        await FollowingAsync(root, reader, history, result, raw, token);
        return result;
    }

    internal static async Task RefuseAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, SafeProjectorOperationRf3Cursors cursors, CancellationToken token)
    {
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            await SafeProjectorOperationRf3Errors.SdkAsync(await reader.Sdk.QueryAsync(Request(history.State, cursors.SdkQuery), token),
                ErrorCode.CursorExpired, InvalidQuery);
        }, token);
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            await SafeProjectorOperationRf3Errors.McpAsync(await reader.Mcp.CallAsync(McpCallerTools.QueryExecute,
                Request(history.State, cursors.McpQuery), token), ErrorCode.CursorExpired, history.State);
        }, token);
        var feedRequest = new ReadChangeFeedRequest(history.State.Partition, history.State.Collection, Cursor: cursors.Feed);
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            await SafeProjectorOperationRf3Errors.SdkAsync(await reader.Sdk.ReadChangesAsync(feedRequest, token),
                ErrorCode.TokenInvalidated, InvalidFeed);
        }, token);
        await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
        {
            await SafeProjectorOperationRf3Errors.McpAsync(await reader.Mcp.CallAsync(McpCallerTools.ChangesRead,
                feedRequest, token), ErrorCode.TokenInvalidated, history.State);
        }, token);
    }

    internal static async Task FollowingAsync(RequestCqrsRf3Callers root, RequestCqrsRf3Callers reader,
        SafeProjectorOperationRf3History history, SafeProjectorOperationRf3Cursors cursors, bool raw, CancellationToken token)
    {
        foreach (var official in new[] { false, true })
        {
            await SafeProjectorOperationRf3Images.ObserveAsync(root, history, async () =>
            {
                var request = Request(history.State, official ? cursors.McpQuery : cursors.SdkQuery);
                var actual = official ? (await McpCallerAssertions.SuccessAsync<QueryPage>(await reader.Mcp.CallAsync(
                    McpCallerTools.QueryExecute, request, token))).Value
                    : await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.QueryAsync(request, token));
                await Assert.That(actual.CutPosition).IsGreaterThan(SafeProjectorOperationRf3Protocol.PositivePublicPosition);
                await SqlRf3Protocol.EqualAsync(new QueryPage(
                    [new(SafeProjectorOperationRf3Protocol.Second, SafeProjectorOperationRf3Protocol.InitialRevision,
                        SafeProjectorOperationRf3Protocol.SecondJson, !raw, raw ? [] : SafeProjectorOperationRf3Protocol.RedactedPaths)],
                    null, actual.CutPosition, FullScan), actual);
            }, token);
        }
    }

    private static QueryRequest Request(SafeProjectorOperationRf3State state, string? cursor = null)
        => new(state.Partition, QueryPrefix + state.Collection + QuerySuffix, AllowFullScan: true, Cursor: cursor);
}
