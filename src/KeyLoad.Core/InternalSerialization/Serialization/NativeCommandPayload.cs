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
    public ReadOnlyMemory<byte> Authority
    {
        get; init;
    }

    [Orleans.Id(NativeCommandContract.SignatureId)]
    public ReadOnlyMemory<byte> Signature
    {
        get; init;
    }

    [Orleans.Id(NativeCommandContract.RetryDecisionsId)]
    public ReadOnlyMemory<byte> RetryDecisions
    {
        get; init;
    }

    [Orleans.Id(NativeCommandContract.ResourceAdmissionId)]
    public ReadOnlyMemory<byte> ResourceAdmission
    {
        get; init;
    }

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
