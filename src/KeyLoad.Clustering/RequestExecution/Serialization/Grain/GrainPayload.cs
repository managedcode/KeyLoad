using KeyLoad.InternalSerialization;

namespace KeyLoad.Clustering;

/// <summary>Admits and reads the typed operation carried by one Orleans request envelope.</summary>
internal static class GrainPayload
{
    internal static object Copy(object payload) => Own(payload, ValidationProfile.PublicInputElements);

    internal static object Strict(object payload) => Own(payload, ValidationProfile.Strict);

    internal static object Own(object payload, ValidationProfile profile)
    {
        Validate(payload, profile);
        return payload;
    }

    internal static void Validate(object payload) => Validate(payload, ValidationProfile.PublicInputElements);

    private static void Validate(object payload, ValidationProfile profile)
    {
        try
        {
            ContractValidation.Validate(payload, profile);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            throw GrainFailureDiagnostics.InvalidPayload(GrainFailureStage.PayloadSyntax);
        }
    }

    internal static T Read<T>(object payload)
        => payload is T value ? value : throw GrainFailureDiagnostics.InvalidPayload(GrainFailureStage.TypedPayloadDecode);

    internal static T ReadCommand<T>(object payload) => Exact<T>(payload, GrainFailureStage.TypedPayloadDecode);

    internal static T ReadPublicInput<T>(object payload) => Exact<T>(payload, GrainFailureStage.TypedPayloadDecode);

    internal static object ReadCommandInput(OperationKind kind, object payload)
    {
        try
        {
            return KeyLoad.Core.DatabaseCommandInputTypes.Require(kind, payload);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            throw GrainFailureDiagnostics.InvalidPayload(GrainFailureStage.TypedPayloadDecode);
        }
    }

    internal static void RequireNoDto(object payload)
    {
        if (Exact<int>(payload, GrainFailureStage.RequiredNullPayload) != GrainContracts.NoDtoMarker)
        {
            throw GrainFailureDiagnostics.InvalidPayload(GrainFailureStage.RequiredNullPayload);
        }
    }

    private static T Exact<T>(object payload, GrainFailureStage stage)
        => payload is T value && payload.GetType() == typeof(T) ? value : throw GrainFailureDiagnostics.InvalidPayload(stage);
}
