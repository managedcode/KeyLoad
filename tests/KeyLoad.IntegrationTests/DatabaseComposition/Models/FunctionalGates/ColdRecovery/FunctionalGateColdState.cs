using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record FunctionalGateColdState(PartitionRef Partition, EntityRef Anchor,
    McpPersistedIdentity Writer, McpPersistedIdentity Reader, McpPersistedIdentity QueueOperator,
    FunctionalGateColdModel[] Models, AdminNodeSnapshot[] Analytics)
{
    internal FunctionalGateReferenceBindings ReferenceBindings { get; init; } = new();
    internal FunctionalGateAnalyticsColdUser[] UserAnalytics { get; init; } = [];
    internal McpPersistedIdentity[] AdditionalIdentities { get; init; } = [];
    internal FunctionalGateColdWorkload[] Workloads { get; init; } = [];
}

internal sealed record FunctionalGateColdWorkload(McpPersistedIdentity Identity, CommandRequest Command,
    CommitReceipt Receipt, DocumentReadResult Expected);

internal sealed record FunctionalGateColdModel(int Index, CommandRequest Command, CommitReceipt Receipt,
    SqlOperationRequest TableCreate, ResourceDefinition TableDefinition, SqlOperationRequest TableInsert,
    CommitReceipt TableReceipt, FunctionalGateColdBlob Blob, EventRecord Event, SourceEventRecord TopicEvent,
    MessageInspectionResult Message);

internal sealed record FunctionalGateColdBlob(BeginBlobUploadRequest Begin, BlobCommitResult<BlobUploadInfo> Begun,
    FunctionalGateColdPart[] Parts, CompleteBlobUploadRequest Complete,
    BlobCommitResult<BlobMetadata> Published, byte[] Bytes);

internal sealed record FunctionalGateColdPart(WriteBlobPartRequest Request, BlobCommitResult<BlobUploadInfo> Result);
