using System.Text.Json;
using ManagedCode.Communication;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineOperations
{
    internal static Result<object> Apply<T>(DatabaseEngine database, OperationKind kind, T payload,
        Guid id, DateTimeOffset time, string principal = RuntimeQueueDeadlineProtocol.Root)
    {
        var operation = database.OwnCommand(database.CreateCommand(kind, id, principal, time, JsonSerializer.Serialize(payload, JsonDefaults.Options)));
        return database.Apply(database.PrepareQueueRetryOperation(operation));
    }

    internal static Result<object> Batch(DatabaseEngine database, CommandRequest command,
        DateTimeOffset time, string principal = RuntimeQueueDeadlineProtocol.Worker)
        => Apply(database, OperationKind.Batch, command, command.CommandId, time, principal);

    internal static void ConfigureWorker(DatabaseEngine database, PartitionRef partition, DateTimeOffset time,
        Capability capability, long epoch)
    {
        var principal = new PrincipalRecord(RuntimeQueueDeadlineProtocol.Worker, partition.TenantId,
            [new(partition.DatabaseId, RuntimeQueueDeadlineProtocol.Queue, capability)], [])
        {
            PolicyEpoch = epoch
        };
        Apply(database, OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal), Guid.NewGuid(), time)
            .Get<PrincipalRecord>();
    }
}
