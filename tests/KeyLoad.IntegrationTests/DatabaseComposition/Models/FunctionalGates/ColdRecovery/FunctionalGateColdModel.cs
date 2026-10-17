namespace KeyLoad.IntegrationTests.Features.DatabaseComposition;

internal sealed record FunctionalGateColdModel(int Index, CommandRequest Command, CommitReceipt Receipt,
    SqlOperationRequest TableCreate, ResourceDefinition TableDefinition, SqlOperationRequest TableInsert,
    CommitReceipt TableReceipt, FunctionalGateColdBlob Blob, EventRecord Event, SourceEventRecord TopicEvent,
    MessageInspectionResult Message);
