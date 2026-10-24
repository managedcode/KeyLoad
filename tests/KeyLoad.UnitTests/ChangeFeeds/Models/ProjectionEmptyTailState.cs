using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.ChangeFeeds;

internal sealed record ProjectionEmptyTailState(TestDatabase Creation, PartitionRef Partition, ProjectionConsumerRef Consumer,
    ProjectionConsumerDefinition Definition, Command<DatabaseCommand> Empty, Result<object> EmptyResult,
    Command<DatabaseCommand> Source, Result<object> SourceResult, CommitReceipt SourceReceipt,
    Command<DatabaseCommand> Rejected, Result<object> Rejection, Command<DatabaseCommand> Complete,
    Result<object> Completion, Command<DatabaseCommand> Filtered, Result<object> Advancement,
    string[] Image, long Position);
