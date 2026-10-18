using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed record ProjectionConsumerIdentityContinuation(Result<object> Completed,
    Command<DatabaseCommand> Filtered, Result<object> Advanced, string[] Image, long Position);
