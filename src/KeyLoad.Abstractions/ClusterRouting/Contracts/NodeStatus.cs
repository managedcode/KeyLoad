namespace KeyLoad;

/// <summary>Reports this physical store, native Orleans membership count and configured data-copy requirement.</summary>
/// <param name="NodeId">Persisted physical store identifier.</param>
/// <param name="Incarnation">Persisted database incarnation.</param>
/// <param name="Position">Local ZoneTree position; positions on different stores are not interchangeable read tokens.</param>
/// <param name="SiloCount">Active silos observed by native Orleans membership.</param>
/// <param name="ReplicaCount">Required persisted copies for a successful write.</param>
/// <param name="Durability">Actual database durability contract.</param>
/// <param name="RoutingReady">Whether native runtime and database startup completed.</param>
/// <param name="ProcessId">Operating system process identifier.</param>
/// <param name="ReadGeneration">Current physical store read generation.</param>
[Orleans.GenerateSerializer, Orleans.Alias(NativeContractAliases.NodeStatus)]
public sealed record NodeStatus([property: Orleans.Id(0)] string NodeId,
    [property: Orleans.Id(1)] Guid Incarnation, [property: Orleans.Id(2)] long Position,
    [property: Orleans.Id(3)] int SiloCount, [property: Orleans.Id(4)] int ReplicaCount,
    [property: Orleans.Id(5)] DurabilityProfile Durability, [property: Orleans.Id(6)] bool RoutingReady,
    [property: Orleans.Id(7)] int ProcessId, [property: Orleans.Id(8)] long ReadGeneration);
