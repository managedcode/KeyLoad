using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed record ProjectionPartitionOutcome(Command<DatabaseCommand> Operation, Result<object> Result);
