using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterReplication;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.RelationalStorage;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobSupersessionPublicTrial
{
    internal sealed record Version(BeginBlobUploadRequest Begin, BlobCommitResult<BlobUploadInfo> Begun,
        WriteBlobPartRequest Part, BlobCommitResult<BlobUploadInfo> Written,
        CompleteBlobUploadRequest Complete, BlobCommitResult<BlobMetadata> Completed);

    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        var versions = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async callers =>
            {
                var scenario = await RelationalSqlClusterScenario.CreateAsync(callers.Sdk, token);
                var blob = new BlobRef(scenario.Partition, RelationalSqlClusterTokens.Blobs, "supersession-public");
                var first = await PublishAsync(callers, blob, 0, new byte[] { 1, 3, 5, 7 }, token);
                var second = await PublishAsync(callers, blob, 1, new byte[] { 2, 4, 6, 8, 10, 12, 14, 16 }, token);
                await RequireAsync(callers, first, second, token);
                return (First: first, Second: second);
            }, token);
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await ColdAsync(fixture, token);
            _ = await RequestCqrsClusterCallers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node2,
                fixture.AdminKey, async callers =>
                {
                    await RequireAsync(callers, versions.First, versions.Second, token);
                    return true;
                }, token);
        }
    }

    private static async Task<Version> PublishAsync(RequestCqrsClusterCallers callers, BlobRef blob,
        long expectedRevision, byte[] bytes, CancellationToken token)
    {
        var upload = Guid.NewGuid();
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, upload, bytes.Length, expectedRevision);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(begin, token));
        var part = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, 0, bytes, BlobIntegrity.PartHash(bytes));
        var written = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.WriteBlobPartAsync(part, token));
        var hash = BlobIntegrity.NextHash(begun.Value.IntegrityHash, part.Ordinal, bytes.Length, part.Sha256);
        await Assert.That(written.Value.IntegrityHash).IsEqualTo(hash);
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, upload, hash);
        var completed = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
            await callers.Mcp.CallAsync("keyload_blobs_complete_upload", complete, token))).Value;
        await Assert.That(completed.Value.Revision).IsEqualTo(expectedRevision + 1);
        await Assert.That(completed.Value.Blob).IsEqualTo(blob);
        await Assert.That(completed.Value.VersionId).IsEqualTo(upload);
        await Assert.That(completed.Value.Length).IsEqualTo((long)bytes.Length);
        await Assert.That(completed.Value.PartCount).IsEqualTo(1);
        await Assert.That(completed.Value.IntegrityHash).IsEqualTo(hash);
        return new(begin, begun, part, written, complete, completed);
    }

    private static async Task RequireAsync(RequestCqrsClusterCallers callers, Version first,
        Version second, CancellationToken token)
    {
        await BlobSupersessionPublicAssertions.ReplayAsync(callers, first, token);
        await BlobSupersessionPublicAssertions.CurrentAsync(callers, second, token);
        await BlobSupersessionPublicAssertions.ReplayAsync(callers, second, token);
        await BlobSupersessionPublicAssertions.CurrentAsync(callers, second, token);
        await BlobSupersessionPublicAssertions.StaleAsync(callers, first, token);
        await BlobSupersessionPublicAssertions.CurrentAsync(callers, second, token);
    }

    private static async Task ColdAsync(ClusterFixture fixture, CancellationToken token)
    {
        string[] nodes = [McpCallerProtocol.Node1, McpCallerProtocol.Node2, McpCallerProtocol.Node3];
        var failures = new List<Exception>();
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.KillServerAsync(node, "blob-supersession-public", token), failures);
        }
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(async () => await fixture.BeginProcessRestartAsync(node, token), failures);
        }
        foreach (var node in nodes)
        {
            await ServerFailureObserver.ObserveAsync(() => fixture.RestartServerAsync(node, token), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
