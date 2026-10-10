using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.Core.Features.InternalSerialization;

[Orleans.GenerateSerializer]
[Orleans.Alias(NativeCommandContract.Alias)]
internal sealed record NativeCommandPayload(
    [property: Orleans.Id(NativeCommandContract.ValueId)] ReadOnlyMemory<byte> Value,
    [property: Orleans.Id(NativeCommandContract.ErrorId)] ErrorCode? Error = null,
    [property: Orleans.Id(NativeCommandContract.SafeDetailId)] string? SafeDetail = null)
{
    [Orleans.Id(NativeCommandContract.AuthorityId)]
    public ReadOnlyMemory<byte> Authority { get; init; }

    [Orleans.Id(NativeCommandContract.SignatureId)]
    public ReadOnlyMemory<byte> Signature { get; init; }

    [Orleans.Id(NativeCommandContract.RetryDecisionsId)]
    public ReadOnlyMemory<byte> RetryDecisions { get; init; }

    [Orleans.Id(NativeCommandContract.TransferProofId)]
    public ReadOnlyMemory<byte> TransferProof { get; init; }

    internal static T Read<T>(ReplicatedOperation operation)
    {
        var payload = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);
        if (payload.Error is { } error)
        {
            throw Errors.Fail(error, payload.SafeDetail ?? NativeCommandContract.InvalidMarker);
        }
        return NativeSerialization.Deserialize<T>(payload.Value.Span, NativeValidationProfile.PublicInputElements);
    }
}

internal static class NativeCommandContract
{
    internal const string Alias = "keyload.core.v1.NativeCommandPayload";
    internal const string InvalidMarker = "The native command failure marker is invalid.";
    internal const string InvalidJson = "The operation contains invalid protocol JSON.";
    internal const string Unsupported = "The operation is unsupported.";
    internal const uint ValueId = 0;
    internal const uint ErrorId = 1;
    internal const uint SafeDetailId = 2;
    internal const uint AuthorityId = 3;
    internal const uint SignatureId = 4;
    internal const uint RetryDecisionsId = 5;
    internal const uint TransferProofId = 6;
    internal const string MissingAuthority = "The native command authority is missing.";
    internal const string InvalidSignature = "The native command authority signature is invalid.";
    internal const string MismatchedAuthority = "The native command authority does not match its scope or payload.";
}
