using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.ChangeFeeds;

internal sealed record ProjectionCheckpointRetentionState(ProjectionConsumerIdentityState Source,
    Command<DatabaseCommand> Pinned, Result<object> PinFailure, Command<DatabaseCommand> Probe, string[] DirtyImage, long DirtyPosition);
