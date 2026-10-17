using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Features.InternalSerialization;

namespace KeyLoad.Core;

/// <summary>Classifies only the exact bounded native runtime-journal identity bootstrap as control work.</summary>
internal static class RuntimeJournalBootstrapAdmission
{
    internal static bool IsControl(ReplicatedOperation operation, int payloadJsonBytes, int maximumControlPayloadBytes)
    {
        if (!IsRuntimeJournal(operation))
        {
            return CommandAdmissionGovernor.IsControl(operation.Kind);
        }
        if (!WithinOuterControlBound(operation, payloadJsonBytes, maximumControlPayloadBytes))
        {
            return false;
        }

        var payload = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);
        return IsControl(operation, payload, payloadJsonBytes, maximumControlPayloadBytes);
    }

    internal static bool IsControl(ReplicatedOperation operation, NativeCommandPayload inspectedPayload,
        int payloadJsonBytes, int maximumControlPayloadBytes)
    {
        if (!IsRuntimeJournal(operation))
        {
            return CommandAdmissionGovernor.IsControl(operation.Kind);
        }
        ArgumentNullException.ThrowIfNull(inspectedPayload);
        if (!WithinOuterControlBound(operation, payloadJsonBytes, maximumControlPayloadBytes)
            || inspectedPayload.Value.Length > maximumControlPayloadBytes || inspectedPayload.Error is not null)
        {
            return false;
        }

        var mutation = NativeSerialization.Deserialize<RuntimeJournalMutation>(inspectedPayload.Value.Span,
            NativeValidationProfile.PublicInputElements);
        if (mutation.Action != RuntimeJournalAction.BootstrapIdentity)
        {
            return false;
        }

        try
        {
            RuntimeJournalMutationValidation.RequireCollections(mutation);
            RuntimeJournalMutationValidation.RequireBootstrapShape(mutation);
            return true;
        }
        catch (KeyLoadException exception) when (exception.Code == ErrorCode.Validation)
        {
            return false;
        }
    }

    private static bool IsRuntimeJournal(ReplicatedOperation operation) => operation.Kind == OperationKind.RuntimeJournal;

    private static bool WithinOuterControlBound(ReplicatedOperation operation, int payloadJsonBytes,
        int maximumControlPayloadBytes)
        => payloadJsonBytes <= maximumControlPayloadBytes
            && operation.NativePayload.Length <= maximumControlPayloadBytes;
}
