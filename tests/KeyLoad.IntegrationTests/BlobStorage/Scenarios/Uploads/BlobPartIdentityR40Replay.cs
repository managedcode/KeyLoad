using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ManagedCode.Communication;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPartIdentityR40Replay
{
    internal const string PartTool = "keyload_blobs_write_part";
    private const string BeginTool = "keyload_blobs_begin_upload";
    private const string CompleteTool = "keyload_blobs_complete_upload";
    private const string DetailProperty = "detail";
    private const string ChangedDetail = "The command ID was already used with different content.";

    internal static async Task RunAsync(RequestCqrsClusterCallers callers, BlobPartIdentityR40State state, CancellationToken token)
    {
        await ChangedAsync(callers, state, token);
        await OriginalAsync(callers, state, state.Begin, state.Begun, BeginTool, ct => callers.Sdk.BeginBlobUploadAsync(state.Begin, ct), token);
        await OriginalAsync(callers, state, state.First, state.WrittenFirst, PartTool, ct => callers.Sdk.WriteBlobPartAsync(state.First, ct), token);
        if (state.Tail is { } tail && state.WrittenTail is { } written)
        {
            await OriginalAsync(callers, state, tail, written, PartTool, ct => callers.Sdk.WriteBlobPartAsync(tail, ct), token);
        }
        if (state.Complete is { } complete && state.Completed is { } completed)
        {
            await OriginalAsync(callers, state, complete, completed, CompleteTool, ct => callers.Sdk.CompleteBlobUploadAsync(complete, ct), token);
        }
    }

    private static async Task OriginalAsync<T>(RequestCqrsClusterCallers callers, BlobPartIdentityR40State state,
        object request, T expected, string tool, Func<CancellationToken, Task<Result<T>>> call, CancellationToken token)
    {
        await SqlClusterProtocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await call(token)));
        await BlobPartIdentityR40Assertions.CurrentAsync(callers, state, token);
        await SqlClusterProtocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<T>(await callers.Mcp.CallAsync(tool, request, token))).Value);
        await BlobPartIdentityR40Assertions.CurrentAsync(callers, state, token);
    }

    private static async Task ChangedAsync(RequestCqrsClusterCallers callers, BlobPartIdentityR40State state, CancellationToken token)
    {
        var result = await callers.Sdk.WriteBlobPartAsync(state.Changed, token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Conflict));
        await Assert.That(result.Problem?.Detail).IsEqualTo(ChangedDetail);
        await BlobPartIdentityR40Assertions.CurrentAsync(callers, state, token);
        var reply = await callers.Mcp.CallAsync(PartTool, state.Changed, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.Conflict, dispatched: true);
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error).GetProperty(DetailProperty).GetString()).IsEqualTo(ChangedDetail);
        await BlobPartIdentityR40Assertions.CurrentAsync(callers, state, token);
    }
}
