namespace KeyLoad;

/// <summary>Reports node identity, replication progress, and readiness.</summary>
/// <param name="NodeId">The configured node identifier.</param>
/// <param name="Incarnation">The unique identifier for this process incarnation.</param>
/// <param name="Applied">The highest committed position applied by this node.</param>
/// <param name="Leader">The current leader identifier, if known.</param>
/// <param name="Voters">The number of voting nodes in the cluster.</param>
/// <param name="Durability">The configured durability profile.</param>
/// <param name="RoutingReady">Whether the node is ready to receive routed requests.</param>
/// <param name="ProcessId">The operating system process identifier.</param>
/// <param name="ReadGeneration">The generation of the node's read view.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.NodeStatus)]
public sealed record NodeStatus([property: Orleans.Id(0)] string NodeId, [property: Orleans.Id(1)] Guid Incarnation, [property: Orleans.Id(2)] long Applied, [property: Orleans.Id(3)] string? Leader, [property: Orleans.Id(4)] int Voters, [property: Orleans.Id(5)] DurabilityProfile Durability, [property: Orleans.Id(6)] bool RoutingReady, [property: Orleans.Id(7)] int ProcessId,
    [property: Orleans.Id(8)] long ReadGeneration = NodeStatus.DefaultReadGeneration)
{
    private const int DefaultReadGeneration = 0;
}
