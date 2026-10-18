using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobUnderfilledR47Refusals
{
    private const string SdkDetail = "The blob upload conflicts with its current state.";
    private const string McpDetail = "The database operation could not be completed.";
    internal const string CompleteTool = "keyload_blobs_complete_upload";

    internal static async Task BothAsync(RequestCqrsClusterCallers callers,
        BlobUnderfilledR47State state, CancellationToken token)
    {
        var result = await callers.Sdk.CompleteBlobUploadAsync(state.Premature, token);
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.Conflict, SdkDetail), result.Problem!);
        await BlobPartIdentityR40Assertions.CurrentAsync(callers, state.Original, token);
        var reply = await callers.Mcp.CallAsync(CompleteTool, state.Premature, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.Conflict, dispatched: true);
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error)
            .GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(McpDetail);
        await BlobPartIdentityR40Assertions.CurrentAsync(callers, state.Original, token);
    }
}
