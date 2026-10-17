using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobPartialReadBoundaryPublicTrial
{
    internal const int TailLength = 17;
    internal const int PatternModulo = 239;
    internal const int InteriorLength = 7;
    internal sealed record Publication(BeginBlobUploadRequest Begin, BlobCommitResult<BlobUploadInfo> Begun,
        WriteBlobPartRequest[] Parts, BlobCommitResult<BlobUploadInfo>[] Written,
        CompleteBlobUploadRequest Complete, BlobCommitResult<BlobMetadata> Completed);

    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async callers =>
            {
                var scenario = await RelationalSqlRf3Scenario.CreateAsync(callers.Sdk, token);
                var blob = new BlobRef(scenario.Partition, RelationalSqlRf3Tokens.Blobs, "partial-boundary-public");
                var publication = await PublishAsync(callers, blob, token);
                await BlobPartialReadBoundaryPublicAssertions.HealthyAsync(callers, publication, token);
                await BlobPartialReadBoundaryPublicAssertions.RefusalsAsync(callers, publication, token);
                await BlobPartialReadBoundaryPublicAssertions.ReplayAsync(callers, publication, token);
                await BlobPartialReadBoundaryPublicAssertions.HealthyAsync(callers, publication, token);
                return true;
            }, token);
    }

    private static async Task<Publication> PublishAsync(RequestCqrsRf3Callers callers, BlobRef blob,
        CancellationToken token)
    {
        var length = BlobLimits.RawPartBytes + TailLength;
        var upload = Guid.NewGuid();
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, upload, length, 0);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(begin, token));
        var parts = new WriteBlobPartRequest[2];
        var written = new BlobCommitResult<BlobUploadInfo>[2];
        var hash = begun.Value.IntegrityHash;
        for (var ordinal = 0; ordinal < parts.Length; ordinal++)
        {
            var start = ordinal * BlobLimits.RawPartBytes;
            var bytes = Enumerable.Range(start, ordinal == 0 ? BlobLimits.RawPartBytes : TailLength)
                .Select(index => checked((byte)(index % PatternModulo))).ToArray();
            var part = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, ordinal, bytes, BlobIntegrity.PartHash(bytes));
            parts[ordinal] = part;
            written[ordinal] = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.WriteBlobPartAsync(part, token));
            hash = BlobIntegrity.NextHash(hash, ordinal, bytes.Length, part.Sha256);
            await Assert.That(written[ordinal].Value.IntegrityHash).IsEqualTo(hash);
        }
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, upload, hash);
        var completed = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
            await callers.Mcp.CallAsync("keyload_blobs_complete_upload", complete, token))).Value;
        await Assert.That(completed.Value.Blob).IsEqualTo(blob);
        await Assert.That(completed.Value.Revision).IsEqualTo(1L);
        await Assert.That(completed.Value.Length).IsEqualTo((long)length);
        await Assert.That(completed.Value.PartCount).IsEqualTo(parts.Length);
        await Assert.That(completed.Value.VersionId).IsEqualTo(upload);
        await Assert.That(completed.Value.IntegrityHash).IsEqualTo(hash);
        return new(begin, begun, parts, written, complete, completed);
    }
}
