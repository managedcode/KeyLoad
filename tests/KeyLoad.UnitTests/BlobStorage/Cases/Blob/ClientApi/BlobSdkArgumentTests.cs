using KeyLoad.Client;

namespace KeyLoad.UnitTests.Features.BlobStorage;

/// <summary>AC-AISQL-012: every public blob adapter rejects missing arguments before transport work.</summary>
internal sealed class BlobSdkArgumentTests
{
    private const string ApiKey = "blob-argument-test-key";
    private const string TenantId = "tenant";
    private const string DatabaseId = "database";
    private const string DomainId = "domain";
    private const string PartitionKey = "partition";
    private const string Resource = "files";
    private const string ObjectId = "object";
    private const int FirstOrdinal = 0;
    private const long FirstRevision = 1;
    private const long AbsentRevision = 0;
    private const long RangeOffset = 0;
    private const int RangeCount = 1;
    private static readonly BlobRef Blob = new(new(TenantId, DatabaseId, DomainId, PartitionKey), Resource, ObjectId);
    private static readonly byte[] Part = [42];

    /// <summary>Missing requests produce the exact argument failure synchronously for reads and writes.</summary>
    /// <param name="operation">The public typed blob method under test.</param>
    [Test]
    [Arguments(nameof(BlobClientExtensions.GetBlobMetadataAsync))]
    [Arguments(nameof(BlobClientExtensions.ListBlobsAsync))]
    [Arguments(nameof(BlobClientExtensions.ReadBlobRangeAsync))]
    [Arguments(nameof(BlobClientExtensions.GetBlobUploadInfoAsync))]
    [Arguments(nameof(BlobClientExtensions.BeginBlobUploadAsync))]
    [Arguments(nameof(BlobClientExtensions.WriteBlobPartAsync))]
    [Arguments(nameof(BlobClientExtensions.CompleteBlobUploadAsync))]
    [Arguments(nameof(BlobClientExtensions.AbortBlobUploadAsync))]
    [Arguments(nameof(BlobClientExtensions.DeleteBlobAsync))]
    [Arguments(nameof(BlobClientExtensions.ReclaimBlobAsync))]
    public async Task AcAisql012MissingRequestIsRejectedBeforeHttp(string operation)
    {
        // A genuine client without a base address cannot reach the network if a guard regresses.
        using var http = new HttpClient();
        var client = new KeyLoadClient(http, ApiKey, UnitClientOptions.Execution());
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var failure = Assert.ThrowsExactly<ArgumentNullException>(() => _ = Invoke(operation, client, true, cancellationToken));
        await Assert.That(failure.ParamName).IsEqualTo("request");
    }

    /// <summary>Missing receivers are classified before either valid or missing request processing.</summary>
    /// <param name="operation">The public typed blob method under test.</param>
    [Test]
    [Arguments(nameof(BlobClientExtensions.GetBlobMetadataAsync))]
    [Arguments(nameof(BlobClientExtensions.ListBlobsAsync))]
    [Arguments(nameof(BlobClientExtensions.ReadBlobRangeAsync))]
    [Arguments(nameof(BlobClientExtensions.GetBlobUploadInfoAsync))]
    [Arguments(nameof(BlobClientExtensions.BeginBlobUploadAsync))]
    [Arguments(nameof(BlobClientExtensions.WriteBlobPartAsync))]
    [Arguments(nameof(BlobClientExtensions.CompleteBlobUploadAsync))]
    [Arguments(nameof(BlobClientExtensions.AbortBlobUploadAsync))]
    [Arguments(nameof(BlobClientExtensions.DeleteBlobAsync))]
    [Arguments(nameof(BlobClientExtensions.ReclaimBlobAsync))]
    public async Task AcAisql012MissingClientIsRejectedBeforeRequestProcessing(string operation)
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var missingClient = Assert.ThrowsExactly<ArgumentNullException>(() => _ = Invoke(operation, null!, false, cancellationToken));
        var bothMissing = Assert.ThrowsExactly<ArgumentNullException>(() => _ = Invoke(operation, null!, true, cancellationToken));
        await Assert.That(missingClient.ParamName).IsEqualTo("client");
        await Assert.That(bothMissing.ParamName).IsEqualTo("client");
    }

    private static Task Invoke(string operation, KeyLoadClient client, bool missingRequest, CancellationToken cancellationToken)
        => operation switch
        {
            nameof(BlobClientExtensions.GetBlobMetadataAsync) => client.GetBlobMetadataAsync(missingRequest ? null! : new(Blob), cancellationToken),
            nameof(BlobClientExtensions.ListBlobsAsync) => client.ListBlobsAsync(missingRequest ? null! : new(Blob.Partition, Resource), cancellationToken),
            nameof(BlobClientExtensions.ReadBlobRangeAsync) => client.ReadBlobRangeAsync(missingRequest ? null! : new(Blob, FirstRevision, RangeOffset, RangeCount), cancellationToken),
            nameof(BlobClientExtensions.GetBlobUploadInfoAsync) => client.GetBlobUploadInfoAsync(missingRequest ? null! : new(Blob, Guid.NewGuid()), cancellationToken),
            nameof(BlobClientExtensions.BeginBlobUploadAsync) => client.BeginBlobUploadAsync(missingRequest ? null! : new(Guid.NewGuid(), Blob, Guid.NewGuid(), Part.Length, AbsentRevision), cancellationToken),
            nameof(BlobClientExtensions.WriteBlobPartAsync) => client.WriteBlobPartAsync(missingRequest ? null! : new(Guid.NewGuid(), Blob, Guid.NewGuid(), FirstOrdinal, Part, BlobIntegrity.PartHash(Part)), cancellationToken),
            nameof(BlobClientExtensions.CompleteBlobUploadAsync) => client.CompleteBlobUploadAsync(missingRequest ? null! : new(Guid.NewGuid(), Blob, Guid.NewGuid(), BlobIntegrity.PartHash(Part)), cancellationToken),
            nameof(BlobClientExtensions.AbortBlobUploadAsync) => client.AbortBlobUploadAsync(missingRequest ? null! : new(Guid.NewGuid(), Blob, Guid.NewGuid()), cancellationToken),
            nameof(BlobClientExtensions.DeleteBlobAsync) => client.DeleteBlobAsync(missingRequest ? null! : new(Guid.NewGuid(), Blob, FirstRevision), cancellationToken),
            nameof(BlobClientExtensions.ReclaimBlobAsync) => client.ReclaimBlobAsync(missingRequest ? null! : new(Guid.NewGuid(), Blob, Guid.NewGuid()), cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unknown public blob operation.")
        };
}
