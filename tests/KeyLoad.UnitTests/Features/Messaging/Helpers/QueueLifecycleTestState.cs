using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed record QueueLifecycleTestState(PartitionRef Partition, DateTimeOffset Time)
{
    internal Func<ReplicatedOperation, OperationResult>? NativeSubmit { get; init; }
    internal OperationResult SubmitOriginal(DatabaseEngine database, ReplicatedOperation operation)
        => NativeSubmit is { } submit ? submit(operation) : database.Apply(operation);

    internal QueueLaneRef Lane => new(Partition, QueueLifecycleTestProtocol.Queue);
    internal List<(ReplicatedOperation Operation, OperationResult Result)> Successes { get; } = [];
    internal List<(ReplicatedOperation Operation, OperationResult Result)> Failures { get; } = [];
    internal CommandRequest? CancelledCommand { get; set; }
    internal Delivery? OriginalPending { get; set; }
    internal Delivery? OriginalHeld { get; set; }
    internal OperationResult Execute<T>(DatabaseEngine database, OperationKind kind, T payload,
        Guid id, string principal = QueueLifecycleTestProtocol.Administrator)
    {
        var operation = new ReplicatedOperation(id, kind, principal, Time, JsonSerializer.Serialize(payload, JsonDefaults.Options));
        var result = SubmitOriginal(database, operation);
        if (result.Error is null)
        { Successes.Add((operation, result)); }
        else
        { Failures.Add((operation, result)); }
        return result;
    }
    internal OperationResult Batch(DatabaseEngine database, params Mutation[] mutations)
    {
        var id = Guid.NewGuid();
        return Execute(database, OperationKind.Batch, new CommandRequest(id, Partition, [.. mutations]), id);
    }
}
