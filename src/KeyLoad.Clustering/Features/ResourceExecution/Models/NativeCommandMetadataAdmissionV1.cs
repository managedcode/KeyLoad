namespace KeyLoad.Clustering;

/// <summary>Signed numeric bounds for one original private phase receiver metadata frame.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(NativeCommandMetadataProtocol.AdmissionAlias)]
internal sealed record NativeCommandMetadataAdmissionV1(
    [property: Orleans.Id(NativeCommandMetadataProtocol.VersionField)] int Version,
    [property: Orleans.Id(NativeCommandMetadataProtocol.MaximumReadBytesField)] long MaximumReadBytes,
    [property: Orleans.Id(NativeCommandMetadataProtocol.MaximumExaminedRecordsField)] int MaximumExaminedRecords,
    [property: Orleans.Id(NativeCommandMetadataProtocol.MaximumResultBytesField)] int MaximumResultBytes);
