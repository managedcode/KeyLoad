using KeyLoad.Core;
using KeyLoad.Replication;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorFirstAppendValidation
{
    private const long FirstNativeTerm = 1;
    private const string Invalid = "The original event vector first Admit requires its actual appended result.";

    internal static EventVectorFirstIssuanceResult Require(object result, EventVectorControlPhase phase,
        EventVectorAdmissionPolicy admission)
    {
        if (result is not EventVectorFirstAdmitAppendResult appended
            || phase.Action != EventVectorControlAction.AdmitFirstIntent
            || appended.OriginalParentRequestId == Guid.Empty
            || appended.OriginalParentRequestId != phase.OriginalParentRequestId
            || appended.AppendedTerm < FirstNativeTerm || appended.OriginalOutcome is null
            || appended.OriginalOutcome.Error is not null
            || appended.OriginalOutcome.NativeValue is not EventVectorFirstIssuanceResult original
            || original.Issuance is null || original.Receipt is null
            || appended.OperationId != original.NativeCommandId
            || original.Receipt.CommandId != appended.OperationId
            || original.Receipt.Token.Position != appended.AppendedIndex
            || original.Issuance.OriginalParentRequestId != appended.OriginalParentRequestId
            || EventVectorControlOperationIdentity.Create(phase, original.Issuance.PrincipalId,
                admission).CommandId != appended.OperationId)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        return original;
    }
}
