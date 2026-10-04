using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>AC-AISQL-006: canonical chunked blobs share actual SDK, SQL and official MCP publication and range reads.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SqlRf3BlobParityTests(ClusterFixture fixture)
{
    private const string BeginTool = "keyload_blobs_begin_upload";
    private const string PartTool = "keyload_blobs_write_part";
    private const string CompleteTool = "keyload_blobs_complete_upload";
    private const string RangeTool = "keyload_blobs_read_range";
    private const int TailLength = 9;
    private const int FirstOrdinal = 0;
    private const int SecondOrdinal = 1;
    private const int PublishedPartCount = 2;
    private const long AbsentRevision = 0;
    private const byte FirstByte = 42;
    private const byte SecondByte = 43;
    private const int RangeOffsetBeforeBoundary = 3;
    private const int RangeLength = 7;
    private static readonly byte[] ExpectedRange = [FirstByte, FirstByte, FirstByte, SecondByte, SecondByte, SecondByte, SecondByte];

    [Test]
    public async Task AcAisql006AbsentBlobMetadataAndEmptyListAreCanonicalAcrossSqlSdkAndMcp()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey);
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(sdk, deadline.Token);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, fixture.AdminKey, deadline.Token);
        var request = new BlobMetadataRequest(new(scenario.Partition, RelationalSqlRf3Tokens.Blobs, RelationalSqlRf3Tokens.BlobId));
        await Assert.That(await McpCallerAssertions.SdkSuccessAsync(await sdk.GetBlobMetadataAsync(request, deadline.Token))).IsNull();
        var sql = SqlRf3Protocol.Call(scenario.Partition, SqlRf3Protocol.BlobMetadataTool, request);
        await Assert.That(await SqlRf3Protocol.SdkAsync<BlobMetadata?>(sdk, sql, deadline.Token)).IsNull();
        await Assert.That(await SqlRf3Protocol.McpAsync<BlobMetadata?>(mcp, sql, deadline.Token)).IsNull();
        var list = new BlobListRequest(scenario.Partition, RelationalSqlRf3Tokens.Blobs);
        var native = await McpCallerAssertions.SdkSuccessAsync(await sdk.ListBlobsAsync(list, deadline.Token));
        var listSql = SqlRf3Protocol.Call(scenario.Partition, SqlRf3Protocol.BlobListTool, list);
        await SqlRf3Protocol.EqualAsync(native, await SqlRf3Protocol.SdkAsync<BlobListPage>(sdk, listSql, deadline.Token));
        await SqlRf3Protocol.EqualAsync(native, await SqlRf3Protocol.McpAsync<BlobListPage>(mcp, listSql, deadline.Token));
        await Assert.That(native.Items).IsEmpty();
        await Assert.That(native.NextAfterId).IsNull();
    }

    [Test]
    public async Task AcAisql006SqlPublishesTwoRealPartsAndAllClientsReadTheExactCrossPartRange()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey);
        var scenario = await RelationalSqlRf3Scenario.CreateAsync(sdk, deadline.Token);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, deadline.Token);
        var blob = new BlobRef(scenario.Partition, RelationalSqlRf3Tokens.Blobs, RelationalSqlRf3Tokens.BlobId);
        var metadata = await PublishAsync(sdk, mcp, blob, deadline.Token);
        var request = new BlobReadRequest(blob, metadata.Revision,
            BlobLimits.RawPartBytes - RangeOffsetBeforeBoundary, RangeLength);
        var native = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadBlobRangeAsync(request, deadline.Token));
        var sql = SqlRf3Protocol.Call(scenario.Partition, RangeTool, request);
        var actual = await SqlRf3Protocol.SdkAsync<BlobReadResult>(sdk, sql, deadline.Token);
        var officialSql = await SqlRf3Protocol.McpAsync<BlobReadResult>(mcp, sql, deadline.Token);
        var officialDirect = await McpCallerAssertions.SuccessAsync<BlobReadResult>(await mcp.CallAsync(RangeTool, request, deadline.Token));
        await Assert.That(native.Bytes.Span.SequenceEqual(ExpectedRange)).IsTrue();
        await SqlRf3Protocol.EqualAsync(native, actual);
        await SqlRf3Protocol.EqualAsync(native, officialSql);
        await SqlRf3Protocol.EqualAsync(native, officialDirect.Value);
        await Assert.That(native.Offset).IsEqualTo(request.Offset);
        await SqlRf3Protocol.EqualAsync(metadata, native.Metadata);
        await VerifyMetadataAndListAsync(sdk, mcp, metadata, deadline.Token);
    }

    private static async Task<BlobMetadata> PublishAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        BlobRef blob, CancellationToken cancellationToken)
    {
        var uploadId = Guid.NewGuid();
        var firstBytes = new byte[BlobLimits.RawPartBytes];
        var tailBytes = new byte[TailLength];
        Array.Fill(firstBytes, FirstByte);
        Array.Fill(tailBytes, SecondByte);
        var length = firstBytes.Length + tailBytes.Length;
        var begin = new BeginBlobUploadRequest(Guid.NewGuid(), blob, uploadId, length, AbsentRevision);
        var begun = await SqlRf3Protocol.SdkAsync<BlobCommitResult<BlobUploadInfo>>(sdk,
            SqlRf3Protocol.Call(blob.Partition, BeginTool, begin), cancellationToken);
        var first = new WriteBlobPartRequest(Guid.NewGuid(), blob, uploadId, FirstOrdinal, firstBytes, BlobIntegrity.PartHash(firstBytes));
        var acceptedFirst = await SqlRf3Protocol.SdkAsync<BlobCommitResult<BlobUploadInfo>>(sdk,
            SqlRf3Protocol.Call(blob.Partition, PartTool, first), cancellationToken);
        var firstRetry = await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await mcp.CallAsync(PartTool, first, cancellationToken));
        await SqlRf3Protocol.EqualAsync(acceptedFirst, firstRetry.Value);
        var tail = new WriteBlobPartRequest(Guid.NewGuid(), blob, uploadId, SecondOrdinal, tailBytes, BlobIntegrity.PartHash(tailBytes));
        var acceptedTail = await McpCallerAssertions.SdkSuccessAsync(await sdk.WriteBlobPartAsync(tail, cancellationToken));
        var expectedHash = BlobIntegrity.NextHash(begun.Value.IntegrityHash, first.Ordinal, first.Bytes.Length, first.Sha256);
        expectedHash = BlobIntegrity.NextHash(expectedHash, tail.Ordinal, tail.Bytes.Length, tail.Sha256);
        await Assert.That(acceptedTail.Value.IntegrityHash).IsEqualTo(expectedHash);
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, uploadId, expectedHash);
        var published = await SqlRf3Protocol.McpAsync<BlobCommitResult<BlobMetadata>>(mcp,
            SqlRf3Protocol.Call(blob.Partition, CompleteTool, complete), cancellationToken);
        var nativeRetry = await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteBlobUploadAsync(complete, cancellationToken));
        await SqlRf3Protocol.EqualAsync(published, nativeRetry);
        await Assert.That(published.Value.PartCount).IsEqualTo(PublishedPartCount);
        await Assert.That(published.Value.Length).IsEqualTo(length);
        await Assert.That(published.Value.IntegrityHash).IsEqualTo(expectedHash);
        await Assert.That(published.Receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        return published.Value;
    }

    private static async Task VerifyMetadataAndListAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        BlobMetadata metadata, CancellationToken cancellationToken)
    {
        var request = new BlobMetadataRequest(metadata.Blob);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetBlobMetadataAsync(request, cancellationToken));
        var sql = SqlRf3Protocol.Call(metadata.Blob.Partition, SqlRf3Protocol.BlobMetadataTool, request);
        await SqlRf3Protocol.EqualAsync(metadata, actual);
        await SqlRf3Protocol.EqualAsync(metadata, await SqlRf3Protocol.McpAsync<BlobMetadata>(mcp, sql, cancellationToken));
        var list = new BlobListRequest(metadata.Blob.Partition, metadata.Blob.Resource);
        var native = await McpCallerAssertions.SdkSuccessAsync(await sdk.ListBlobsAsync(list, cancellationToken));
        var listSql = SqlRf3Protocol.Call(metadata.Blob.Partition, SqlRf3Protocol.BlobListTool, list);
        var official = await SqlRf3Protocol.McpAsync<BlobListPage>(mcp, listSql, cancellationToken);
        await Assert.That(native.Items).HasSingleItem();
        await SqlRf3Protocol.EqualAsync(metadata, native.Items[0]);
        await SqlRf3Protocol.EqualAsync(native, official);
    }
}
