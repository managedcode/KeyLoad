using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.BlobStorage;

internal static class BlobRejectedContentPublicTrial
{
    internal const string IncorrectHash = "0000000000000000000000000000000000000000000000000000000000000000";
    internal const string OriginalPartSha256 = "f4e3f0b04771c047e227c9ecaba65d3fe2fd0e1eee0a7552b956d1a7c535a7cf";
    internal const string ChangedPartSha256 = "5c6b4d6f8644dab4bab0b174d790a4587f9fa157c817d404e959e31d181c45b3";
    internal static readonly byte[] OriginalBytes = [16, 32, 48, 64];

    internal static async Task RunAsync(ClusterFixture fixture, CancellationToken token)
    {
        _ = await RequestCqrsRf3Callers.RunOwnedAsync(fixture.App, McpCallerProtocol.Node1,
            fixture.AdminKey, async callers =>
            {
                var scenario = await RelationalSqlRf3Scenario.CreateAsync(callers.Sdk, token);
                var blob = new BlobRef(scenario.Partition, RelationalSqlRf3Tokens.Blobs, "rejected-content-public");
                await ExecuteAsync(callers, blob, token);
                return true;
            }, token);
    }

    private static async Task ExecuteAsync(RequestCqrsRf3Callers callers, BlobRef blob, CancellationToken token)
    {
        var upload = Guid.NewGuid();
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, upload, OriginalBytes.Length, 0L);
        var begun = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.BeginBlobUploadAsync(begin, token));
        var invalid = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, 0, OriginalBytes, IncorrectHash);
        await BlobRejectedContentPublicAssertions.PartRefusedAsync(callers, invalid, ErrorCode.Validation,
            () => BlobRejectedContentPublicAssertions.HiddenAsync(callers, begin, begun.Value, token), token);
        var part = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, 0, OriginalBytes,
            OriginalPartSha256);
        var written = await McpCallerAssertions.SdkSuccessAsync(await callers.Sdk.WriteBlobPartAsync(part, token));
        var chain = BlobRejectedContentPublicAssertions.IndependentNext(begun.Value.IntegrityHash);
        await BlobRejectedContentPublicAssertions.WrittenAsync(begin, begun.Value, written.Value, chain);
        byte[] different = [16, 32, 48, 65];
        var changed = new WriteBlobPartRequest(Guid.NewGuid(), blob, upload, 0, different, ChangedPartSha256);
        await BlobRejectedContentPublicAssertions.PartRefusedAsync(callers, changed, ErrorCode.Conflict,
            () => BlobRejectedContentPublicAssertions.HiddenAsync(callers, begin, written.Value, token), token);
        var wrong = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, upload, IncorrectHash);
        await BlobRejectedContentPublicAssertions.CompleteRefusedAsync(callers, wrong,
            () => BlobRejectedContentPublicAssertions.HiddenAsync(callers, begin, written.Value, token), token);
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, upload, chain);
        var completed = (await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobMetadata>>(
            await callers.Mcp.CallAsync("keyload_blobs_complete_upload", complete, token))).Value;
        var published = new BlobSupersessionPublicTrial.Version(begin, begun, part, written, complete, completed);
        await BlobRejectedContentPublicState.PublishedAsync(callers, published, token);
        await BlobRejectedContentPublicReplay.RunAsync(callers, published, invalid, changed, wrong, token);
    }
}
