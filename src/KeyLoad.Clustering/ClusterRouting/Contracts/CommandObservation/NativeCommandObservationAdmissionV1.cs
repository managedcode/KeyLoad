namespace KeyLoad.Clustering;

/// <summary>Reserves finite original outcome-read work before the command can have effects.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(GrainNativeContracts.CommandObservationAdmissionAlias)]
internal sealed record NativeCommandObservationAdmissionV1(
    [property: Orleans.Id(0)] int Version,
    [property: Orleans.Id(1)] Guid ObservationId,
    [property: Orleans.Id(2)] long MaximumReadBytes,
    [property: Orleans.Id(3)] int MaximumExaminedRecords,
    [property: Orleans.Id(4)] int MaximumResultBytes);
