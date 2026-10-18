using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed record OutboxHeadAuthorityContinuation(Command<DatabaseCommand> Healthy,
    CommitReceipt Receipt, Result<object> HealthyResult, Result<object> Purged, string[] Image, long Position);
