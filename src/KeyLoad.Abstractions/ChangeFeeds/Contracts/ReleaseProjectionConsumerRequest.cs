namespace KeyLoad;

/// <summary>Requests release of a projection consumer.</summary>
/// <param name="CommandId">The idempotent command identifier.</param>
/// <param name="Consumer">The projection consumer to release.</param>
/// <param name="IndexGeneration">The expected index generation for the release.</param>
[Orleans.GenerateSerializer]
[Orleans.Alias(NativeContractAliases.ReleaseProjectionConsumerRequest)]
public sealed record ReleaseProjectionConsumerRequest([property: Orleans.Id(0)] Guid CommandId, [property: Orleans.Id(1)] ProjectionConsumerRef Consumer, [property: Orleans.Id(2)] long IndexGeneration);
