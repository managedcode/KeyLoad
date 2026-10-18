namespace KeyLoad.Core.Features.InternalSerialization;

[Orleans.GenerateSerializer, Orleans.Alias(NativeAuthorityContract.Alias)]
internal sealed record NativeCommandAuthority(
    [property: Orleans.Id(NativeAuthorityContract.PurposeId)] string Purpose,
    [property: Orleans.Id(NativeAuthorityContract.IncarnationId)] Guid Incarnation,
    [property: Orleans.Id(NativeAuthorityContract.OperationId)] Guid OperationId,
    [property: Orleans.Id(NativeAuthorityContract.KindId)] OperationKind Kind,
    [property: Orleans.Id(NativeAuthorityContract.PrincipalId)] string PrincipalId,
    [property: Orleans.Id(NativeAuthorityContract.FingerprintId)] string Fingerprint,
    [property: Orleans.Id(NativeAuthorityContract.ValueHashId)] ReadOnlyMemory<byte> ValueHash,
    [property: Orleans.Id(NativeAuthorityContract.ErrorId)] ErrorCode? Error,
    [property: Orleans.Id(NativeAuthorityContract.SafeDetailId)] string? SafeDetail,
    [property: Orleans.Id(NativeAuthorityContract.RetryDecisionHashId)] ReadOnlyMemory<byte> RetryDecisionHash = default,
    [property: Orleans.Id(NativeAuthorityContract.ResourceAdmissionHashId)] ReadOnlyMemory<byte> ResourceAdmissionHash = default);
