using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.FunctionalTests.ChangeFeeds;

internal sealed record ProjectionConcurrentCommitState(ProjectionConsumerIdentityState Source,
    Command<DatabaseCommand> First, Result<object> FirstResult, Command<DatabaseCommand> Second, Result<object> SecondResult,
    bool WinnerIsFirst, Command<DatabaseCommand> Filtered, Result<object> Advanced, string[] Image, long Position);
