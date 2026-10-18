using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed record ProjectionConsumerIdentityContinuation(Result<object> Completed,
    Command<DatabaseCommand> Filtered, Result<object> Advanced, string[] Image, long Position);
