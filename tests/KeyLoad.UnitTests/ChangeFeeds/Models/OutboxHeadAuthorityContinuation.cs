using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.ChangeFeeds;

internal sealed record OutboxHeadAuthorityContinuation(Command<DatabaseCommand> Healthy,
    CommitReceipt Receipt, Result<object> HealthyResult, Result<object> Purged, string[] Image, long Position);
