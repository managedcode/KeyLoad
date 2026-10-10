using System.Text.Json;
using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class QueueDeadlineNativeOperations
{
    internal static OperationResult Apply<T>(DatabaseEngine database, OperationKind kind, T payload,
        Guid id, DateTimeOffset time, string principal = QueueDeadlineNativeProtocol.Root)
    {
        var operation = database.NormalizeOperation(new(id, kind, principal, time,
            JsonSerializer.Serialize(payload, JsonDefaults.Options)));
        return database.Apply(database.PrepareQueueRetryOperation(operation));
    }

    internal static OperationResult Batch(DatabaseEngine database, CommandRequest command,
        DateTimeOffset time, string principal = QueueDeadlineNativeProtocol.Worker)
        => Apply(database, OperationKind.Batch, command, command.CommandId, time, principal);

    internal static void ConfigureWorker(DatabaseEngine database, PartitionRef partition, DateTimeOffset time,
        Capability capability, long epoch)
    {
        var principal = new PrincipalRecord(QueueDeadlineNativeProtocol.Worker, partition.TenantId,
            [new(partition.DatabaseId, QueueDeadlineNativeProtocol.Queue, capability)], [])
        { PolicyEpoch = epoch };
        Apply(database, OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(principal), Guid.NewGuid(), time)
            .Get<PrincipalRecord>();
    }
}
