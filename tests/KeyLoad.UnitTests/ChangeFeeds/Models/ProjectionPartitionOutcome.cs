using ManagedCode.Communication;
using ManagedCode.Communication.Commands;
namespace KeyLoad.UnitTests.ChangeFeeds;

internal sealed record ProjectionPartitionOutcome(Command<DatabaseCommand> Operation, Result<object> Result);
