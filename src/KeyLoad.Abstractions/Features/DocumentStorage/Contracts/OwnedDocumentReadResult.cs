namespace KeyLoad;

/// <summary>Private trusted-transport document result; never a public remote authority token.</summary>
/// <param name="Document">The authorized projected document.</param>
/// <param name="NodeId">Actual receiving store identity.</param>
/// <param name="Incarnation">Actual receiving store incarnation.</param>
/// <param name="ReadGeneration">Actual receiving tree generation.</param>
/// <param name="Placement">The same-cut partition placement.</param>
/// <param name="PolicyEpoch">Fresh receiving principal epoch.</param>
/// <param name="AppliedPosition">Actual canonical applied cut.</param>
/// <param name="StorePosition">Independent local storage position.</param>
[Orleans.GenerateSerializer, Orleans.Alias(RemoteDocumentAliases.Result)]
public sealed record OwnedDocumentReadResultV1(
    [property: Orleans.Id(0)] DocumentResult? Document,
    [property: Orleans.Id(1)] Guid NodeId,
    [property: Orleans.Id(2)] Guid Incarnation,
    [property: Orleans.Id(3)] long ReadGeneration,
    [property: Orleans.Id(4)] AtomicPartitionPlacementResolution Placement,
    [property: Orleans.Id(5)] long PolicyEpoch,
    [property: Orleans.Id(6)] long AppliedPosition,
    [property: Orleans.Id(7)] long StorePosition);
