namespace KeyLoad.Core.Features.InternalSerialization;

internal static class NativeAuthorityContract
{
    internal const string Alias = "keyload.core.native-command.authority.v1";
    internal const string Purpose = "keyload-core-native-command-v1";
    internal const int DigestBytes = 32;
    internal const int PurposeId = 0;
    internal const int IncarnationId = 1;
    internal const int OperationId = 2;
    internal const int KindId = 3;
    internal const int PrincipalId = 4;
    internal const int FingerprintId = 5;
    internal const int ValueHashId = 6;
    internal const int ErrorId = 7;
    internal const int SafeDetailId = 8;
}

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
    [property: Orleans.Id(NativeAuthorityContract.SafeDetailId)] string? SafeDetail);
