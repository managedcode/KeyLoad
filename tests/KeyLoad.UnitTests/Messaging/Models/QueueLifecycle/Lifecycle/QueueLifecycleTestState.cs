using System.Text.Json;
using KeyLoad.Core;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueLifecycleTestState(PartitionRef Partition, DateTimeOffset Time)
{
    internal Func<Command<DatabaseCommand>, Result<object>>? NativeSubmit
    {
        get; init;
    }
    internal Result<object> SubmitOriginal(DatabaseEngine database, Command<DatabaseCommand> operation)
        => NativeSubmit is { } submit ? submit(operation) : database.Apply(operation);

    internal QueueLaneRef Lane => new(Partition, QueueLifecycleTestProtocol.Queue);
    internal List<(Command<DatabaseCommand> Operation, Result<object> Result)> Successes { get; } = [];
    internal List<(Command<DatabaseCommand> Operation, Result<object> Result)> Failures { get; } = [];
    internal CommandRequest? CancelledCommand
    {
        get; set;
    }
    internal Delivery? OriginalPending
    {
        get; set;
    }
    internal Delivery? OriginalHeld
    {
        get; set;
    }
    internal Result<object> Execute<T>(DatabaseEngine database, OperationKind kind, T payload,
        Guid id, string principal = QueueLifecycleTestProtocol.Administrator)
    {
        var operation = database.CreateCommand(kind, id, principal, Time, JsonSerializer.Serialize(payload, JsonDefaults.Options));
        var result = SubmitOriginal(database, operation);
        if (result.Error is null)
        {
            Successes.Add((operation, result));
        }
        else
        {
            Failures.Add((operation, result));
        }
        return result;
    }
    internal Result<object> Batch(DatabaseEngine database, params Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        return Execute(database, OperationKind.Batch, new CommandRequest(id, Partition, [.. mutations]), id);
    }
}
