using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobListRowPolicyPublicAssertions
{
    internal static async Task OwnerAAsync(RequestCqrsRf3Callers reader,
        BlobSupersessionPublicTrial.Version[] versions, CancellationToken token)
    {
        await PageAsync(reader, versions, null, versions[1].Completed.Value, "b-visible", token);
        await PageAsync(reader, versions, "b-visible", versions[3].Completed.Value, "d-visible", token);
        await PageAsync(reader, versions, "d-visible", null, null, token);
    }

    internal static async Task PageAsync(RequestCqrsRf3Callers reader, BlobSupersessionPublicTrial.Version[] versions,
        string? after, BlobMetadata? item, string? next, CancellationToken token)
    {
        var scope = versions[0].Begin.Blob;
        var request = new BlobListRequest(scope.Partition, scope.Resource, 1, after);
        var expected = new BlobListPage(item is null ? [] : [item], next);
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await reader.Sdk.ListBlobsAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobListPage>(
            await reader.Mcp.CallAsync(SqlRf3Protocol.BlobListTool, request, token))).Value);
    }

    internal static async Task AllAsync(RequestCqrsRf3Callers admin,
        BlobSupersessionPublicTrial.Version[] versions, CancellationToken token)
    {
        var scope = versions[0].Begin.Blob;
        var request = new BlobListRequest(scope.Partition, scope.Resource, 4);
        var expected = new BlobListPage([.. versions.Select(value => value.Completed.Value)], "d-visible");
        await SqlRf3Protocol.EqualAsync(expected, await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ListBlobsAsync(request, token)));
        await SqlRf3Protocol.EqualAsync(expected, (await McpCallerAssertions.SuccessAsync<BlobListPage>(
            await admin.Mcp.CallAsync(SqlRf3Protocol.BlobListTool, request, token))).Value);
        for (var index = 0; index < versions.Length; index++)
        {
            var version = versions[index];
            var read = new BlobReadRequest(version.Begin.Blob, 1, 0, 2);
            var bytes = new byte[] { checked((byte)(index + 11)), checked((byte)(index + 31)) };
            var result = new BlobReadResult(version.Completed.Value, 0, bytes);
            await SqlRf3Protocol.EqualAsync(result, await McpCallerAssertions.SdkSuccessAsync(await admin.Sdk.ReadBlobRangeAsync(read, token)));
            await SqlRf3Protocol.EqualAsync(result, (await McpCallerAssertions.SuccessAsync<BlobReadResult>(
                await admin.Mcp.CallAsync("keyload_blobs_read_range", read, token))).Value);
        }
    }

    internal static async Task DeniedAsync(RequestCqrsRf3Callers admin, RequestCqrsRf3Callers reader,
        BlobSupersessionPublicTrial.Version[] versions, CancellationToken token)
    {
        var scope = versions[0].Begin.Blob;
        foreach (var after in new string?[] { null, "b-visible" })
        {
            var request = new BlobListRequest(scope.Partition, scope.Resource, 1, after);
            var sdk = await reader.Sdk.ListBlobsAsync(request, token);
            await Assert.That(sdk.IsFailed).IsTrue();
            await Assert.That(sdk.Value).IsNull();
            await Assert.That(sdk.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
            await AllAsync(admin, versions, token);
            await McpCallerAssertions.ErrorAsync(await reader.Mcp.CallAsync(SqlRf3Protocol.BlobListTool, request, token),
                ErrorCode.PermissionDenied, dispatched: true);
            await AllAsync(admin, versions, token);
        }
    }
}
