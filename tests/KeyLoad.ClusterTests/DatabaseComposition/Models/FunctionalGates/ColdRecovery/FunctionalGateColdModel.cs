namespace KeyLoad.ClusterTests.DatabaseComposition;

internal sealed record FunctionalGateColdModel(int Index, CommandRequest Command, CommitReceipt Receipt,
    SqlOperationRequest TableCreate, ResourceDefinition TableDefinition, SqlOperationRequest TableInsert,
    CommitReceipt TableReceipt, FunctionalGateFile Blob, EventRecord Event, SourceEventRecord TopicEvent,
    MessageInspectionResult Message);
