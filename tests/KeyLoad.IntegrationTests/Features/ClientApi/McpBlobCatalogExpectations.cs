using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>Independent public BlobStorage schema and effect expectations for native discovery.</summary>
internal static class McpBlobCatalogExpectations
{
    private const string BeginUpload = "keyload_blobs_begin_upload";
    private const string WritePart = "keyload_blobs_write_part";
    private const string CompleteUpload = "keyload_blobs_complete_upload";
    private const string AbortUpload = "keyload_blobs_abort_upload";
    private const string Delete = "keyload_blobs_delete";
    private const string Reclaim = "keyload_blobs_reclaim";
    private const string Metadata = "keyload_blobs_metadata";
    private const string UploadInfo = "keyload_blobs_upload_info";
    private const string ReadRange = "keyload_blobs_read_range";
    private const string List = "keyload_blobs_list";
    private const string Blob = "blob";
    private const string UploadId = "uploadId";
    private const string Length = "length";
    private const string ExpectedRevision = "expectedRevision";
    private const string Ordinal = "ordinal";
    private const string Bytes = "bytes";
    private const string Sha256 = "sha256";
    private const string ExpectedIntegrityHash = "expectedIntegrityHash";
    private const string Offset = "offset";
    private const string Count = "count";
    private const string Resource = "resource";

    internal static ImmutableArray<McpToolExpectation> Entries { get; } =
    [
        Command(BeginUpload, false, [McpCallerProtocol.CommandId, Blob, UploadId, Length, ExpectedRevision]),
        Command(WritePart, false, [McpCallerProtocol.CommandId, Blob, UploadId, Ordinal, Bytes, Sha256]),
        Command(CompleteUpload, true, [McpCallerProtocol.CommandId, Blob, UploadId, ExpectedIntegrityHash]),
        Command(AbortUpload, false, [McpCallerProtocol.CommandId, Blob, UploadId]),
        Command(Delete, true, [McpCallerProtocol.CommandId, Blob, ExpectedRevision]),
        Command(Reclaim, true, [McpCallerProtocol.CommandId, Blob, UploadId]),
        Read(Metadata, [Blob]),
        Read(UploadInfo, [Blob, UploadId]),
        Read(ReadRange, [Blob, ExpectedRevision, Offset, Count]),
        Read(List, [McpDiscoveryProtocol.Partition, Resource])
    ];

    private static McpToolExpectation Command(string name, bool destructive, ImmutableArray<string> fields)
        => new(name, false, true, destructive, McpExpectedBody.Object, false, fields);

    private static McpToolExpectation Read(string name, ImmutableArray<string> fields)
        => new(name, true, true, false, McpExpectedBody.Object, false, fields);
}
