using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed record ProjectionConcurrentCommitState(ProjectionConsumerIdentityState Source,
    Command<DatabaseCommand> First, Result<object> FirstResult, Command<DatabaseCommand> Second, Result<object> SecondResult,
    bool WinnerIsFirst, Command<DatabaseCommand> Filtered, Result<object> Advanced, string[] Image, long Position);
