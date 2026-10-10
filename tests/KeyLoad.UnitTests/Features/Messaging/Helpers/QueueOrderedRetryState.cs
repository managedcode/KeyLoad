using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class QueueOrderedRetryState(PartitionRef partition, DateTimeOffset time, QueueParkedHeadPolicy parkedHead,
    string principal = QueueOrderedRetryProtocol.Root)
{
    internal PartitionRef Partition { get; } = partition;
    internal string Principal { get; } = principal;
    internal Dictionary<Guid, byte[]> HistoricalOutcomes { get; } = [];
    internal byte[]? RestoredResourceBytes { get; set; }
    internal QueueLaneRef Lane { get; } = new(partition, QueueOrderedRetryProtocol.Queue);
    internal DateTimeOffset Time { get; set; } = time;
    internal QueueParkedHeadPolicy ParkedHead { get; } = parkedHead;
    internal List<(ReplicatedOperation Operation, OperationResult Result)> Outcomes { get; } = [];
    internal Delivery? FirstLease { get; set; }
    internal Delivery? SecondLease { get; set; }
    internal OperationResult Execute<T>(DatabaseEngine database, OperationKind kind, T payload, Guid id)
    {
        var operation = database.NormalizeOperation(new(id, kind, Principal, Time,
            JsonSerializer.Serialize(payload, JsonDefaults.Options)));
        operation = database.PrepareQueueRetryOperation(operation);
        var result = database.Apply(operation);
        Outcomes.Add((operation, result));
        return result;
    }
    internal OperationResult Batch(DatabaseEngine database, params Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        return Execute(database, OperationKind.Batch, new CommandRequest(id, Partition, [.. mutations]), id);
    }
}
