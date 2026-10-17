namespace KeyLoad.Clustering;

/// <summary>Preserves the original principal wire grammar and the actual charged authentication work.</summary>
[Orleans.GenerateSerializer, Orleans.Alias(GrainNativeContracts.AdministratorAuthenticationResultAlias)]
internal sealed record NativeAdministratorAuthenticationResultV1(
    [property: Orleans.Id(0)] ReadOnlyMemory<byte> PrincipalPayload,
    [property: Orleans.Id(1)] long ReadBytes,
    [property: Orleans.Id(2)] int ExaminedRecords,
    [property: Orleans.Id(3)] ErrorCode? Error,
    [property: Orleans.Id(4)] string? SafeDetail);
