using KeyLoad.Core;
using KeyLoad.UnitTests.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.Messaging;

internal sealed class SubscriptionOldCutState(PartitionRef partition)
{
    internal const string Topic = "old-cut-topic";
    internal const string Collection = "old-cut-effects";
    internal const string Worker = "old-cut-worker";
    internal const string EffectJson = "{\"completed\":2}";
    internal static readonly DateTimeOffset Epoch = new(2040, 1, 1, 0, 0, 0, TimeSpan.Zero);
    internal ControlledReadClock Clock { get; } = new(Epoch);
    internal SubscriptionRef Group { get; } = new(new(partition, Topic, EventSourceKind.Topic), "processor");
    internal SubscriptionDefinition Definition { get; } = new("root") { Policy = new() { MaxWindow = 3 } };
    internal PrincipalRecord Principal(long epoch, bool write) => new(Worker, partition.TenantId,
        [new(partition.DatabaseId, Topic, Capability.TopicsRead | Capability.SubscriptionsConsume | Capability.SubscriptionsAck),
        new(partition.DatabaseId, Collection, Capability.DocumentsRead | (write ? Capability.DocumentsWrite : Capability.None))], [])
    {
        PolicyEpoch = epoch
    };
    internal Dictionary<ReplicatedOperation, OperationResult> Successes { get; } = [];
    internal ReplicatedOperation? DeniedOperation
    {
        get; set;
    }
    internal byte[] DeniedOutcome { get; set; } = [];
    internal byte[] DeniedLocator { get; set; } = [];
    internal ReceiveSubscriptionRequest OriginalReceive { get; set; } = null!;
    internal ReceiveSubscriptionResult OriginalClaim { get; set; } = null!;
    internal SubscriptionProcessingResult OriginalProcessing { get; set; } = null!;
    internal ReplicatedOperation OriginalProcessingOperation { get; set; } = null!;
    internal CommitReceipt Producer { get; set; } = null!;
    internal CommitReceipt FreshProducer { get; set; } = null!;
    internal CommitReceipt FreshProcessing { get; set; } = null!;
    internal ReplicatedOperation Create<T>(OperationKind kind, T payload, Guid id, string principal = "root")
        => new(id, kind, principal, Clock.GetUtcNow(), System.Text.Json.JsonSerializer.Serialize(payload, JsonDefaults.Options));
    internal OperationResult Apply<T>(DatabaseEngine database, OperationKind kind, T payload, Guid id, string principal = "root")
    {
        var operation = Create(kind, payload, id, principal);
        var result = database.Apply(operation);
        if (result.Error is { } code)
        {
            throw Errors.Fail(code, result.SafeDetail ?? "The owned operation failed.");
        }
        Successes.Add(operation, result);
        return result;
    }
    internal SubscriptionProcessingRequest Processing(string token, string id = "input2")
        => new(Guid.NewGuid(), Group, token, "handler", 1, [new PutDocument(Collection, id, EffectJson, 0)]);
}
