using KeyLoad.Core.Features.RuntimeJournaling.Validation;
using KeyLoad.Core.Features.StoragePartitions.Validation;
using KeyLoad.Features.InternalSerialization;
using ManagedCode.Communication.Commands;

namespace KeyLoad.Core;

/// <summary>Classifies only the exact bounded execution runtime-journal identity bootstrap as control work.</summary>
internal static class RuntimeJournalBootstrapAdmission
{
    internal static bool IsControl(Command<DatabaseCommand> operation, int payloadJsonBytes, int maximumControlPayloadBytes)
    {
        if (!IsRuntimeJournal(operation))
        {
            return CommandAdmissionGovernor.IsControl(operation.Value!.Kind);
        }
        if (!WithinOuterControlBound(operation, payloadJsonBytes, maximumControlPayloadBytes))
        {
            return false;
        }

        var payload = operation.Value!;
        return IsControl(operation, payload, payloadJsonBytes, maximumControlPayloadBytes);
    }

    internal static bool IsControl(Command<DatabaseCommand> operation, DatabaseCommand inspectedPayload,
        int payloadJsonBytes, int maximumControlPayloadBytes)
    {
        if (!IsRuntimeJournal(operation))
        {
            return CommandAdmissionGovernor.IsControl(operation.Value!.Kind);
        }
        ArgumentNullException.ThrowIfNull(inspectedPayload);
        if (!WithinOuterControlBound(operation, payloadJsonBytes, maximumControlPayloadBytes)
            || inspectedPayload.Input.Length > maximumControlPayloadBytes)
        {
            return false;
        }

        var mutation = OrleansSerialization.Deserialize<RuntimeJournalMutation>(inspectedPayload.Input.Span,
            ValidationProfile.PublicInputElements);
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

    private static bool IsRuntimeJournal(Command<DatabaseCommand> operation) => operation.Value!.Kind == OperationKind.RuntimeJournal;

    private static bool WithinOuterControlBound(Command<DatabaseCommand> operation, int payloadJsonBytes,
        int maximumControlPayloadBytes)
        => payloadJsonBytes <= maximumControlPayloadBytes
            && OrleansSerialization.Measure(operation) <= maximumControlPayloadBytes;
}
