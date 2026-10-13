using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobSelectiveReadR43Reads
{
    private const string Tool = "keyload_blobs_read_range";
    private const string Detail = "The principal cannot perform this operation in this scope.";
    private const string DetailKey = "detail";
    internal static async Task MatrixAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers reader,
        BlobSelectiveReadR43State state, bool firstDenied, CancellationToken token)
    {
        for (var index = 0; index < 2; index++)
        {
            var version = state.Versions[index];
            var expected = new BlobReadResult(version.Completed.Value, 0L, BlobSelectiveReadR43State.Bytes(index));
            var request = new BlobReadRequest(version.Begin.Blob, 1L, 0L, expected.Bytes.Length);
            var sdk = await reader.Sdk.ReadBlobRangeAsync(request, token);
            if (index == 0 && firstDenied)
            {
                await Assert.That(sdk.IsFailed).IsTrue();
                await Assert.That(sdk.Value).IsNull();
                await SqlRf3Protocol.EqualAsync(Errors.Problem(ErrorCode.PermissionDenied, Detail), sdk.Problem);
            }
            else
            { await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(sdk)); }
            await CurrentAsync(admin, state, token);
            var reply = await reader.Mcp.CallAsync(Tool, request, token);
            if (index == 0 && firstDenied)
            {
                await McpCallerAssertions.ErrorAsync(reply, ErrorCode.PermissionDenied, dispatched: true);
                await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
                    .GetProperty(DetailKey).GetString()).IsEqualTo(Detail);
                await McpCallerAssertions.DoesNotDiscloseAsync(reply, state.Identity.Secret,
                    Convert.ToBase64String(BlobSelectiveReadR43State.Bytes(index)));
            }
            else
            { await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(reply)).Value); }
            await CurrentAsync(admin, state, token);
        }
    }
    internal static async Task CurrentAsync(RequestCqrsRf3Callers admin,
        BlobSelectiveReadR43State state, CancellationToken token)
    {
        for (var index = 0; index < 2; index++)
        {
            var version = state.Versions[index];
            var bytes = BlobSelectiveReadR43State.Bytes(index);
            var request = new BlobReadRequest(version.Begin.Blob, 1L, 0L, bytes.Length);
            var expected = new BlobReadResult(version.Completed.Value, 0L, bytes);
            await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(
                await admin.Sdk.ReadBlobRangeAsync(request, token)));
            await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(
                await admin.Mcp.CallAsync(Tool, request, token))).Value);
        }
        await SqlRf3Protocol.EqualAsync(state.Outbox, await OutboxAsync(admin, state, token));
    }
    internal static async Task<OutboxStatus> OutboxAsync(RequestCqrsRf3Callers admin,
        BlobSelectiveReadR43State state, CancellationToken token)
    {
        var partition = state.Versions[0].Begin.Blob.Partition;
        var sdk = await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.OutboxStatusAsync(partition, token));
        await SqlRf3Protocol.EqualAsync(sdk, (await McpCallerAssertions.SuccessAsync<OutboxStatus>(
            await admin.Mcp.CallAsync(McpCallerTools.OutboxStatus, new GetOutboxStatusRequest(partition), token))).Value);
        return sdk;
    }
}
