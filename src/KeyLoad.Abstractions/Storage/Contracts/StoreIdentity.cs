namespace KeyLoad.Storage;

/// <summary>Records the persisted format, authority and token-signing identity of a store.</summary>
/// <param name="FormatVersion">Persisted storage format version.</param>
/// <param name="KeyCodecVersion">Encoded key format version.</param>
/// <param name="NodeId">Physical node identity owning this store.</param>
/// <param name="Incarnation">Authority incarnation used to invalidate stale tokens.</param>
/// <param name="SigningKey">Private key material used by server-owned token signing.</param>
/// <param name="Durability">Acknowledgement guarantee supplied by the provider.</param>
/// <param name="DispatchPaused">Whether restored dispatch remains paused.</param>
/// <param name="ReadGeneration">Generation fencing restored or replaced read authority.</param>
/// <param name="MinimumReaderContract">Required reader capability admitted by this execution store.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(ContractAliases.StoreIdentity)]
public sealed record StoreIdentity([property: Orleans.Id(0)] int FormatVersion, [property: Orleans.Id(1)] int KeyCodecVersion, [property: Orleans.Id(2)] Guid NodeId, [property: Orleans.Id(3)] Guid Incarnation,
    [property: Orleans.Id(4)] ReadOnlyMemory<byte> SigningKey, [property: Orleans.Id(5)] DurabilityProfile Durability, [property: Orleans.Id(6)] bool DispatchPaused = false, [property: Orleans.Id(7)] long ReadGeneration = StoreIdentity.DefaultReadGeneration,
    [property: Orleans.Id(8), System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] int MinimumReaderContract = StoreReaderContract.Unspecified)
{
    private const int DefaultReadGeneration = 0;
}
