namespace KeyLoad.Clustering;

/// <summary>Accepted logical receiver metadata work; DATA page work is a separate original grant.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(NativeCommandMetadataProtocol.StatisticsAlias)]
internal sealed record NativeCommandMetadataStatisticsV1(
    [property: Orleans.Id(NativeCommandMetadataProtocol.ReadBytesField)] long ReadBytes,
    [property: Orleans.Id(NativeCommandMetadataProtocol.ExaminedRecordsField)] int ExaminedRecords,
    [property: Orleans.Id(NativeCommandMetadataProtocol.ResultBytesField)] int ResultBytes);
