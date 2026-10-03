using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Orleans;

namespace KeyLoad.UnitTests.Features.BlobStorage;

/// <summary>Independent ADR-038 agent operation vectors, preserving the frozen public names and routes.</summary>
internal static class BlobAgentCases
{
    internal const string Request = "request";
    internal const string CommandIdMember = "commandId";
    internal const string BytesMember = "bytes";
    internal const string AuthorityMember = "principalId";
    internal const string BlobMember = "blob";
    internal const string UploadIdMember = "uploadId";
    internal const string LengthMember = "length";
    internal const string ExpectedRevisionMember = "expectedRevision";
    internal const string AccessMember = "access";
    internal const string OrdinalMember = "ordinal";
    internal const string Sha256Member = "sha256";
    internal const string ExpectedHashMember = "expectedIntegrityHash";
    internal const string MaxPartsMember = "maxParts";
    internal const string PartitionMember = "partition";
    internal const string ResourceMember = "resource";
    internal const string OffsetMember = "offset";
    internal const string CountMember = "count";
    internal const string LimitMember = "limit";
    internal const string AfterIdMember = "afterId";
    internal const string Principal = "root";
    internal const string Resource = "objects";
    internal const string ObjectId = "binary-1";
    private const string Tenant = "tenant";
    private const string Database = "database";
    private const string Domain = "orders";
    private const string PartitionKey = "customer-1";
    private const string StableCommandId = "02338471-1d5a-4ce5-9812-b09a2ff54d19";
    private const string ScopedUploadId = "b7b9e8c4-3c57-4c83-b8fc-c9e7920b9c1e";
    internal const string Begin = "keyload_blobs_begin_upload";
    internal const string Part = "keyload_blobs_write_part";
    internal const string Complete = "keyload_blobs_complete_upload";
    internal const string Abort = "keyload_blobs_abort_upload";
    internal const string Delete = "keyload_blobs_delete";
    internal const string Reclaim = "keyload_blobs_reclaim";
    internal const string Metadata = "keyload_blobs_metadata";
    internal const string UploadInfo = "keyload_blobs_upload_info";
    internal const string Range = "keyload_blobs_read_range";
    internal const string List = "keyload_blobs_list";
    internal const string BeginRoute = "/v1/blobs/uploads/begin";
    internal const string PartRoute = "/v1/blobs/uploads/parts";
    internal const string CompleteRoute = "/v1/blobs/uploads/complete";
    internal const string AbortRoute = "/v1/blobs/uploads/abort";
    internal const string DeleteRoute = "/v1/blobs/delete";
    internal const string ReclaimRoute = "/v1/blobs/reclaim";
    internal const string MetadataRoute = "/v1/blobs/metadata";
    internal const string UploadInfoRoute = "/v1/blobs/uploads/info";
    internal const string RangeRoute = "/v1/blobs/range";
    internal const string ListRoute = "/v1/blobs/list";
    internal static PartitionRef Partition { get; } = new(Tenant, Database, Domain, PartitionKey);
    internal static BlobRef Blob { get; } = new(Partition, Resource, ObjectId);
    internal static Guid CommandId { get; } = Guid.Parse(StableCommandId);
    private static Guid UploadId { get; } = Guid.Parse(ScopedUploadId);
    private static ReadOnlyMemory<byte> Bytes { get; } = new byte[] { 1, 2, 3 };

    internal static ImmutableArray<BlobAgentCase> All() =>
    [
        Command(Begin, BeginRoute, OperationKind.BeginBlobUpload, new BeginBlobUploadRequest(CommandId, Blob, UploadId, Bytes.Length, 0)),
        Command(Part, PartRoute, OperationKind.WriteBlobPart, new WriteBlobPartRequest(CommandId, Blob, UploadId, 0, Bytes, BlobIntegrity.PartHash(Bytes.Span))),
        Command(Complete, CompleteRoute, OperationKind.CompleteBlobUpload, new CompleteBlobUploadRequest(CommandId, Blob, UploadId, BlobIntegrity.PartHash(Bytes.Span))),
        Command(Abort, AbortRoute, OperationKind.AbortBlobUpload, new AbortBlobUploadRequest(CommandId, Blob, UploadId)),
        Command(Delete, DeleteRoute, OperationKind.DeleteBlob, new DeleteBlobRequest(CommandId, Blob, 1)),
        Command(Reclaim, ReclaimRoute, OperationKind.ReclaimBlob, new ReclaimBlobRequest(CommandId, Blob, UploadId)),
        Read(Metadata, MetadataRoute, GrainReadKind.BlobMetadata, new BlobMetadataRequest(Blob)),
        Read(UploadInfo, UploadInfoRoute, GrainReadKind.BlobUploadInfo, new BlobUploadInfoRequest(Blob, UploadId)),
        Read(Range, RangeRoute, GrainReadKind.BlobRange, new BlobReadRequest(Blob, 1, 0, Bytes.Length)),
        Read(List, ListRoute, GrainReadKind.BlobList, new BlobListRequest(Partition, Resource))
    ];

    internal static string[] RequestFields(string name) => name switch
    {
        Begin => [CommandIdMember, BlobMember, UploadIdMember, LengthMember, ExpectedRevisionMember, AccessMember],
        Part => [CommandIdMember, BlobMember, UploadIdMember, OrdinalMember, BytesMember, Sha256Member],
        Complete => [CommandIdMember, BlobMember, UploadIdMember, ExpectedHashMember],
        Abort => [CommandIdMember, BlobMember, UploadIdMember],
        Delete => [CommandIdMember, BlobMember, ExpectedRevisionMember],
        Reclaim => [CommandIdMember, BlobMember, UploadIdMember, MaxPartsMember],
        Metadata => [BlobMember],
        UploadInfo => [BlobMember, UploadIdMember],
        Range => [BlobMember, ExpectedRevisionMember, OffsetMember, CountMember],
        List => [PartitionMember, ResourceMember, LimitMember, AfterIdMember],
        _ => throw new ArgumentException(name, nameof(name))
    };

    private static BlobAgentCase Command<T>(string name, string route, OperationKind kind, T request) =>
        new(name, route, null, kind, JsonSerializer.SerializeToElement(request, JsonDefaults.Options), JsonDefaults.Serialize(request), NativeSerialization.Serialize(request));

    private static BlobAgentCase Read<T>(string name, string route, GrainReadKind kind, T request) =>
        new(name, route, kind, null, JsonSerializer.SerializeToElement(request, JsonDefaults.Options), JsonDefaults.Serialize(request), NativeSerialization.Serialize(request));
}

/// <summary>One operation retaining public JSON arguments/bytes and separately encoded native typed payload.</summary>
internal sealed record BlobAgentCase(string Name, string Route, GrainReadKind? ReadKind, OperationKind? CommandKind,
    JsonElement Request, ReadOnlyMemory<byte> Payload, ReadOnlyMemory<byte> NativePayload)
{
    internal Dictionary<string, JsonElement> Arguments() => new(StringComparer.Ordinal) { [BlobAgentCases.Request] = Request };
}
