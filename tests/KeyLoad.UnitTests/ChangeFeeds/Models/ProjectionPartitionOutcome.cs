using ManagedCode.Communication.Commands;
using ManagedCode.Communication;
namespace KeyLoad.UnitTests.Features.ChangeFeeds;

internal sealed record ProjectionPartitionOutcome(Command<DatabaseCommand> Operation, Result<object> Result);
