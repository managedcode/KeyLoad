namespace KeyLoad.Storage;

/// <summary>Records the persisted authority and token-signing identity of a store.</summary>
/// <param name="NodeId">Physical node identity owning this store.</param>
/// <param name="Incarnation">Authority incarnation used to invalidate stale tokens.</param>
/// <param name="SigningKey">Private key material used by server-owned token signing.</param>
/// <param name="Durability">Acknowledgement guarantee supplied by the provider.</param>
/// <param name="DispatchPaused">Whether restored dispatch remains paused.</param>
/// <param name="ReadGeneration">Generation fencing restored or replaced read authority.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.StoreIdentity)]
public sealed record StoreIdentity([property: Orleans.Id(2)] Guid NodeId, [property: Orleans.Id(3)] Guid Incarnation,
    [property: Orleans.Id(4)] ReadOnlyMemory<byte> SigningKey, [property: Orleans.Id(5)] DurabilityProfile Durability, [property: Orleans.Id(6)] bool DispatchPaused = false, [property: Orleans.Id(7)] long ReadGeneration = StoreIdentity.DefaultReadGeneration)
{
    private const int DefaultReadGeneration = 0;
}
