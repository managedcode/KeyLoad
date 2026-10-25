using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal sealed record ProjectionCheckpointRetentionContinuation(ProjectionCheckpointRetentionState Original,
    Result<object> ProbeFailure, Result<object> Completed, Command<DatabaseCommand> Filtered, Result<object> Advanced,
    Command<DatabaseCommand> Purge, Result<object> Purged, Command<DatabaseCommand> Healthy, Result<object> Produced,
    string[] Image, long Position);
