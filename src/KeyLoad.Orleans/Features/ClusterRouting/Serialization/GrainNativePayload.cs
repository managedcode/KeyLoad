using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.Orleans;

internal static class GrainNativePayload
{
    internal static ReadOnlyMemory<byte> Copy(ReadOnlyMemory<byte> payload, int maximumBytes)
    {
        if (payload.Length > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, GrainRoutingProtocol.InvalidRequest);
        }

        Validate(payload.Span);
        return payload.ToArray();
    }

    internal static void Validate(ReadOnlySpan<byte> payload)
    {
        try
        {
            NativeSerialization.Validate(payload);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            throw GrainFailureDiagnostics.InvalidPayload(GrainFailureStage.PayloadSyntax);
        }
    }

    internal static T Read<T>(ReadOnlyMemory<byte> payload)
        => Decode<T>(payload, GrainFailureStage.TypedPayloadDecode);

    internal static T ReadCommand<T>(ReadOnlyMemory<byte> payload)
        => Decode<T>(payload, GrainFailureStage.TypedPayloadDecode, NativeValidationProfile.PublicInputElements);

    internal static T ReadPublicInput<T>(ReadOnlyMemory<byte> payload)
        => Decode<T>(payload, GrainFailureStage.TypedPayloadDecode, NativeValidationProfile.PublicInputElements);

    internal static void RequireNoDto(ReadOnlyMemory<byte> payload)
    {
        if (Decode<int>(payload, GrainFailureStage.RequiredNullPayload) != GrainNativeContracts.NoDtoMarker)
        {
            throw GrainFailureDiagnostics.InvalidPayload(GrainFailureStage.RequiredNullPayload);
        }
    }

    private static T Decode<T>(ReadOnlyMemory<byte> payload, GrainFailureStage stage,
        NativeValidationProfile profile = NativeValidationProfile.Strict)
    {
        try
        {
            return NativeSerialization.Deserialize<T>(payload.Span, profile);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            throw GrainFailureDiagnostics.InvalidPayload(stage);
        }
    }
}
