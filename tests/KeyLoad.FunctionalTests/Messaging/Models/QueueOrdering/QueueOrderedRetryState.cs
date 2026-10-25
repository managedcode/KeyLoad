using System.Text.Json;
using ManagedCode.Communication;
using ManagedCode.Communication.Commands;

namespace KeyLoad.FunctionalTests.Messaging;

internal sealed class QueueOrderedRetryState(PartitionRef partition, DateTimeOffset time, QueueParkedHeadPolicy parkedHead,
    string principal = QueueOrderedRetryProtocol.Root)
{
    internal PartitionRef Partition { get; } = partition;
    internal string Principal { get; } = principal;
    internal Action<DatabaseEngine, Command<DatabaseCommand>>? BeforeApply
    {
        get; init;
    }
    internal Dictionary<Guid, byte[]> HistoricalOutcomes { get; } = [];
    internal byte[]? RestoredResourceBytes
    {
        get; set;
    }
    internal QueueLaneRef Lane { get; } = new(partition, QueueOrderedRetryProtocol.Queue);
    internal DateTimeOffset Time { get; set; } = time;
    internal QueueParkedHeadPolicy ParkedHead { get; } = parkedHead;
    internal List<(Command<DatabaseCommand> Operation, Result<object> Result)> Outcomes { get; } = [];
    internal Delivery? FirstLease
    {
        get; set;
    }
    internal Delivery? SecondLease
    {
        get; set;
    }
    internal Result<object> Execute<T>(DatabaseEngine database, OperationKind kind, T payload, Guid id)
    {
        var operation = database.OwnCommand(database.CreateCommand(kind, id, Principal, Time, JsonSerializer.Serialize(payload, JsonDefaults.Options)));
        operation = database.PrepareQueueRetryOperation(operation);
        BeforeApply?.Invoke(database, operation);
        var result = database.Apply(operation);
        Outcomes.Add((operation, result));
        return result;
    }
    internal Result<object> Batch(DatabaseEngine database, params Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        return Execute(database, OperationKind.Batch, new CommandRequest(id, Partition, [.. mutations]), id);
    }
}
