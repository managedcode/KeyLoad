using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed record ProjectionCheckpointRetentionContinuation(ProjectionCheckpointRetentionState Original,
    Result<object> ProbeFailure, Result<object> Completed, Command<DatabaseCommand> Filtered, Result<object> Advanced,
    Command<DatabaseCommand> Purge, Result<object> Purged, Command<DatabaseCommand> Healthy, Result<object> Produced,
    string[] Image, long Position);
