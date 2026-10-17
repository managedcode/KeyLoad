namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Actual admitted metadata-frame work and exact native payload bytes covered by the original reply MAC.</summary>
[global::Orleans.GenerateSerializer, global::Orleans.Alias(ResourceRetirementNativeMetadataV1.Alias)]
internal sealed record ResourceRetirementNativeMetadataV1(
    [property: global::Orleans.Id(0)] long ReadBytes,
    [property: global::Orleans.Id(1)] int ExaminedRecords,
    [property: global::Orleans.Id(2)] int ResultBytes)
{
    internal const string Alias = "keyload.resource-retirement.native-metadata.v1";
}
