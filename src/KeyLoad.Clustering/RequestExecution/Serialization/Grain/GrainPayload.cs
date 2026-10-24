using KeyLoad.InternalSerialization;

namespace KeyLoad.Clustering;

internal static class GrainPayload
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
            OrleansSerialization.Validate(payload, typeof(object), ValidationProfile.PublicInputElements);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            throw GrainFailureDiagnostics.InvalidPayload(GrainFailureStage.PayloadSyntax);
        }
    }

    internal static T Read<T>(ReadOnlyMemory<byte> payload)
        => Decode<T>(payload, GrainFailureStage.TypedPayloadDecode);

    internal static T ReadCommand<T>(ReadOnlyMemory<byte> payload)
        => Decode<T>(payload, GrainFailureStage.TypedPayloadDecode, ValidationProfile.PublicInputElements);

    internal static T ReadPublicInput<T>(ReadOnlyMemory<byte> payload)
        => Decode<T>(payload, GrainFailureStage.TypedPayloadDecode, ValidationProfile.PublicInputElements);

    internal static void RequireNoDto(ReadOnlyMemory<byte> payload)
    {
        if (Decode<int>(payload, GrainFailureStage.RequiredNullPayload) != GrainContracts.NoDtoMarker)
        {
            throw GrainFailureDiagnostics.InvalidPayload(GrainFailureStage.RequiredNullPayload);
        }
    }

    private static T Decode<T>(ReadOnlyMemory<byte> payload, GrainFailureStage stage,
        ValidationProfile profile = ValidationProfile.Strict)
    {
        try
        {
            return OrleansSerialization.Deserialize<T>(payload.Span, profile);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            throw GrainFailureDiagnostics.InvalidPayload(stage);
        }
    }
}
