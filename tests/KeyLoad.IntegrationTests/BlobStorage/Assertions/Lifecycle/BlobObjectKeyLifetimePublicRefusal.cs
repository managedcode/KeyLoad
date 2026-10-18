using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobObjectKeyLifetimePublicRefusal
{
    private const string Detail = "detail";
    private const string Capacity = "The blob reservation quota is exhausted.";
    private const string SafeCapacity = "The operation exceeds the available resource capacity.";
    internal static async Task RequireAsync(RequestCqrsClusterCallers callers, BlobObjectKeyLifetimePublicState refused,
        BlobObjectKeyLifetimePublicState[] states, CancellationToken token)
    {
        var denied = await callers.Sdk.BeginBlobUploadAsync(refused.Original, token);
        await Assert.That(denied.IsFailed).IsTrue();
        await Assert.That(denied.Value).IsNull();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.ResourceExhausted));
        await Assert.That(denied.Problem?.Detail).IsEqualTo(Capacity);
        await BlobObjectKeyLifetimePublicAssertions.CurrentAsync(callers, states, token);
        var reply = await callers.Mcp.CallAsync("keyload_blobs_begin_upload", refused.Original, token);
        await McpCallerAssertions.ErrorAsync(reply, ErrorCode.ResourceExhausted, dispatched: true);
        await Assert.That(reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error).GetProperty(Detail).GetString()).IsEqualTo(SafeCapacity);
        await BlobObjectKeyLifetimePublicAssertions.CurrentAsync(callers, states, token);
    }
}
