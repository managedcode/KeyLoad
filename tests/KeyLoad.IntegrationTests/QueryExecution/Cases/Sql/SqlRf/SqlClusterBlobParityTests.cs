using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.RelationalStorage;

namespace KeyLoad.IntegrationTests.Features.QueryExecution;

/// <summary>AC-AISQL-006: canonical chunked blobs share actual SDK, SQL and official MCP publication and range reads.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class SqlClusterBlobParityTests(ClusterFixture fixture)
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
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await RelationalSqlClusterScenario.CreateAsync(sdk, deadline.Token);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, fixture.AdminKey, deadline.Token);
        var request = new BlobMetadataRequest(new(scenario.Partition, RelationalSqlClusterTokens.Blobs, RelationalSqlClusterTokens.BlobId));
        var absent = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetBlobMetadataAsync(request, deadline.Token));
        await Assert.That(absent.Metadata).IsNull();
        var sql = SqlClusterProtocol.Call(scenario.Partition, SqlClusterProtocol.BlobMetadataTool, request);
        var sqlAbsent = await SqlClusterProtocol.SdkAsync<BlobMetadataReadResult>(sdk, sql, deadline.Token);
        var mcpAbsent = await SqlClusterProtocol.McpAsync<BlobMetadataReadResult>(mcp, sql, deadline.Token);
        await Assert.That(sqlAbsent.Metadata).IsNull();
        await Assert.That(mcpAbsent.Metadata).IsNull();
        var list = new BlobListRequest(scenario.Partition, RelationalSqlClusterTokens.Blobs);
        var native = await McpCallerAssertions.SdkSuccessAsync(await sdk.ListBlobsAsync(list, deadline.Token));
        var listSql = SqlClusterProtocol.Call(scenario.Partition, SqlClusterProtocol.BlobListTool, list);
        await SqlClusterProtocol.EqualAsync(native, await SqlClusterProtocol.SdkAsync<BlobListPage>(sdk, listSql, deadline.Token));
        await SqlClusterProtocol.EqualAsync(native, await SqlClusterProtocol.McpAsync<BlobListPage>(mcp, listSql, deadline.Token));
        await Assert.That(native.Items).IsEmpty();
        await Assert.That(native.NextAfterId).IsNull();
    }

    [Test]
    public async Task AcAisql006SqlPublishesTwoRealPartsAndAllClientsReadTheExactCrossPartRange()
    {
        using var deadline = McpCallerDeadline.Create();
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var scenario = await RelationalSqlClusterScenario.CreateAsync(sdk, deadline.Token);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2, fixture.AdminKey, deadline.Token);
        var blob = new BlobRef(scenario.Partition, RelationalSqlClusterTokens.Blobs, RelationalSqlClusterTokens.BlobId);
        var metadata = await PublishAsync(sdk, mcp, blob, deadline.Token);
        var request = new BlobReadRequest(blob, metadata.Revision,
            BlobLimits.RawPartBytes - RangeOffsetBeforeBoundary, RangeLength);
        var native = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadBlobRangeAsync(request, deadline.Token));
        var sql = SqlClusterProtocol.Call(scenario.Partition, RangeTool, request);
        var actual = await SqlClusterProtocol.SdkAsync<BlobReadResult>(sdk, sql, deadline.Token);
        var officialSql = await SqlClusterProtocol.McpAsync<BlobReadResult>(mcp, sql, deadline.Token);
        var officialDirect = await McpCallerAssertions.SuccessAsync<BlobReadResult>(await mcp.CallAsync(RangeTool, request, deadline.Token));
        await Assert.That(native.Bytes.Span.SequenceEqual(ExpectedRange)).IsTrue();
        await SqlClusterProtocol.EqualAsync(native, actual);
        await SqlClusterProtocol.EqualAsync(native, officialSql);
        await SqlClusterProtocol.EqualAsync(native, officialDirect.Value);
        await Assert.That(native.Offset).IsEqualTo(request.Offset);
        await SqlClusterProtocol.EqualAsync(metadata, native.Metadata);
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
        var begun = await SqlClusterProtocol.SdkAsync<BlobCommitResult<BlobUploadInfo>>(sdk,
            SqlClusterProtocol.Call(blob.Partition, BeginTool, begin), cancellationToken);
        var first = new WriteBlobPartRequest(Guid.NewGuid(), blob, uploadId, FirstOrdinal, firstBytes, BlobIntegrity.PartHash(firstBytes));
        var acceptedFirst = await SqlClusterProtocol.SdkAsync<BlobCommitResult<BlobUploadInfo>>(sdk,
            SqlClusterProtocol.Call(blob.Partition, PartTool, first), cancellationToken);
        var firstRetry = await McpCallerAssertions.SuccessAsync<BlobCommitResult<BlobUploadInfo>>(
            await mcp.CallAsync(PartTool, first, cancellationToken));
        await SqlClusterProtocol.EqualAsync(acceptedFirst, firstRetry.Value);
        var tail = new WriteBlobPartRequest(Guid.NewGuid(), blob, uploadId, SecondOrdinal, tailBytes, BlobIntegrity.PartHash(tailBytes));
        var acceptedTail = await McpCallerAssertions.SdkSuccessAsync(await sdk.WriteBlobPartAsync(tail, cancellationToken));
        var expectedHash = BlobIntegrity.NextHash(begun.Value.IntegrityHash, first.Ordinal, first.Bytes.Length, first.Sha256);
        expectedHash = BlobIntegrity.NextHash(expectedHash, tail.Ordinal, tail.Bytes.Length, tail.Sha256);
        await Assert.That(acceptedTail.Value.IntegrityHash).IsEqualTo(expectedHash);
        var complete = new CompleteBlobUploadRequest(Guid.NewGuid(), blob, uploadId, expectedHash);
        var published = await SqlClusterProtocol.McpAsync<BlobCommitResult<BlobMetadata>>(mcp,
            SqlClusterProtocol.Call(blob.Partition, CompleteTool, complete), cancellationToken);
        var nativeRetry = await McpCallerAssertions.SdkSuccessAsync(await sdk.CompleteBlobUploadAsync(complete, cancellationToken));
        await SqlClusterProtocol.EqualAsync(published, nativeRetry);
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
        var sql = SqlClusterProtocol.Call(metadata.Blob.Partition, SqlClusterProtocol.BlobMetadataTool, request);
        await SqlClusterProtocol.EqualAsync(metadata, actual.Metadata);
        var official = await SqlClusterProtocol.McpAsync<BlobMetadataReadResult>(mcp, sql, cancellationToken);
        await SqlClusterProtocol.EqualAsync(metadata, official.Metadata);
        var list = new BlobListRequest(metadata.Blob.Partition, metadata.Blob.Resource);
        var native = await McpCallerAssertions.SdkSuccessAsync(await sdk.ListBlobsAsync(list, cancellationToken));
        var listSql = SqlClusterProtocol.Call(metadata.Blob.Partition, SqlClusterProtocol.BlobListTool, list);
        var officialList = await SqlClusterProtocol.McpAsync<BlobListPage>(mcp, listSql, cancellationToken);
        await Assert.That(native.Items).HasSingleItem();
        await SqlClusterProtocol.EqualAsync(metadata, native.Items[0]);
        await SqlClusterProtocol.EqualAsync(native, officialList);
    }
}
