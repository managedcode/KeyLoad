namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed record ProjectionEmptyTailState(PartitionRef Partition, ProjectionConsumerRef Consumer,
    ProjectionConsumerDefinition Definition, ReplicatedOperation Empty, OperationResult EmptyResult,
    ReplicatedOperation Source, OperationResult SourceResult, CommitReceipt SourceReceipt,
    ReplicatedOperation Rejected, OperationResult Rejection, ReplicatedOperation Complete,
    OperationResult Completion, ReplicatedOperation Filtered, OperationResult Advancement,
    string[] Image, long Position);
