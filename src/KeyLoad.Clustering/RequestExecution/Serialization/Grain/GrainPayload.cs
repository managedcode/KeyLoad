using KeyLoad.InternalSerialization;

namespace KeyLoad.Clustering;

/// <summary>Admits and reads the typed operation carried by one Orleans request envelope.</summary>
internal static class GrainPayload
{
    internal static T Copy<T>(T payload) where T : notnull => Own(payload, ValidationProfile.PublicInputElements);

    internal static T Strict<T>(T payload) where T : notnull => Own(payload, ValidationProfile.Strict);

    internal static T Own<T>(T payload, ValidationProfile profile) where T : notnull
    {
        Validate(payload, profile);
        return payload;
    }

    internal static void Validate<T>(T payload) where T : notnull => Validate(payload, ValidationProfile.PublicInputElements);

    private static void Validate<T>(T payload, ValidationProfile profile) where T : notnull
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

    internal static T Read<T>(object /* KLD0041: The received Payload wire slot is object; validate its requested T before this read handler uses it. */ payload)
        => (payload is T value /* KLD0042: Decode the received native envelope slot as T and reject a mismatched runtime payload before typed execution. */) ? value : throw GrainFailureDiagnostics.InvalidPayload(GrainFailureStage.TypedPayloadDecode);

    internal static T ReadCommand<T>(object /* KLD0041: The current command Payload wire slot is object; require its exact generated request type before command execution. */ payload) => (Exact<T>(payload, GrainFailureStage.TypedPayloadDecode) /* KLD0041: Validate the received generated envelope payload as T at this closed operation boundary before executing typed code. */);

    internal static T ReadPublicInput<T>(object /* KLD0041: Public-input envelopes keep their current Payload field; require the exact admitted DTO type before AST or graph execution. */ payload) => (Exact<T>(payload, GrainFailureStage.TypedPayloadDecode) /* KLD0041: Validate the received generated envelope payload as T at this closed operation boundary before executing typed code. */);

    internal static object /* KLD0041: The current persisted DatabaseCommand.Input schema carries heterogeneous closed-kind DTOs; return that validated canonical input without changing its format. */ ReadCommandInput(OperationKind kind, object /* KLD0041: Validate the received envelope Payload slot against its closed OperationKind before creating the canonical stored command. */ payload)
    {
        try
        {
            return (KeyLoad.Core.DatabaseCommandInputTypes.Require(kind, payload) /* KLD0041: Validate the current DatabaseCommand input slot against its selected closed operation kind before storage execution. */);
        }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Corruption)
        {
            throw GrainFailureDiagnostics.InvalidPayload(GrainFailureStage.TypedPayloadDecode);
        }
    }

    internal static void RequireNoDto(object /* KLD0041: The current no-DTO wire payload is a boxed integer marker; preserve and validate that exact scalar representation. */ payload)
    {
        if ((Exact<int>(payload, GrainFailureStage.RequiredNullPayload) /* KLD0041: Validate the received generated envelope payload as int at this closed operation boundary before executing typed code. */) != GrainContracts.NoDtoMarker)
        {
            throw GrainFailureDiagnostics.InvalidPayload(GrainFailureStage.RequiredNullPayload);
        }
    }

    private static T Exact<T>(object /* KLD0041: The received native Payload field requires an exact runtime T check, including the scalar no-DTO marker, before typed use. */ payload, GrainFailureStage stage)
        => (payload is T value /* KLD0042: Decode the received native envelope slot as T and reject a mismatched runtime payload before typed execution. */) && (payload.GetType() /* KLD0041: Require the received envelope payload to have the exact generated DTO runtime type before accepting the decoded value. */) == typeof(T) ? value : throw GrainFailureDiagnostics.InvalidPayload(stage);
}
