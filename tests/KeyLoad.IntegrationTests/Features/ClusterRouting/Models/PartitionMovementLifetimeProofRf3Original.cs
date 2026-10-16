using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class PartitionMovementLifetimeProofRf3Original(CommandRequest command, EntityRef entity,
    string json, Guid armId, Task producer, Func<Task<CommitReceipt>> result)
{
    internal CommandRequest Command { get; } = command;
    internal EntityRef Entity { get; } = entity;
    internal string Json { get; } = json;
    internal Guid ArmId { get; } = armId;
    internal Task Producer { get; } = producer;
    internal Func<Task<CommitReceipt>> Result { get; } = result;
    internal CommitReceipt? Receipt
    {
        get; set;
    }
    internal RequestCqrsControlledLifetimeStepWitness? Baseline
    {
        get; set;
    }
    internal byte[]? Carrier
    {
        get; set;
    }
    internal HashSet<Guid> NegativeNonces { get; } = [];
}
