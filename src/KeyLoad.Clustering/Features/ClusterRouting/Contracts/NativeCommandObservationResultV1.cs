using System.Collections.Immutable;

namespace KeyLoad.Clustering;

/// <summary>Actual strict stored-outcome observation with its original native work counters.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(GrainNativeContracts.CommandObservationResultAlias)]
internal sealed record NativeCommandObservationResultV1(
    [property: Orleans.Id(0)] OperationResult Result,
    [property: Orleans.Id(1)] long ReadBytes,
    [property: Orleans.Id(2)] int ExaminedRecords,
    [property: Orleans.Id(3)] ImmutableArray<ResourceLifetimeV1> Lifetimes);
