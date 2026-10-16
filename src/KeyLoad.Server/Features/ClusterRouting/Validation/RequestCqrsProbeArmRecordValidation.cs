using System.Text;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ClusterRouting;

internal static class RequestCqrsProbeArmRecordValidation
{
    internal static void Require(RequestCqrsProbeArmRecord value,
        IOptions<RequestProbeExecutionOptions> executionOptions, UTF8Encoding strictUtf8)
    {
        var read = value.ReadKind.HasValue;
        if (value.Version != RequestCqrsProbeProtocol.Version || value.Kind != RequestCqrsProbeProtocol.ArmKind
            || !RequestCqrsProbeOptionsReader.IsSessionId(value.SessionId) || value.ArmId == Guid.Empty
            || !ValidPrincipal(value.PrincipalId, executionOptions, strictUtf8)
            || !Enum.IsDefined(value.Action) || !Enum.IsDefined(value.Phase)
            || value.Phase is RequestCqrsProbePhase.MovementParentStateExecuting or RequestCqrsProbePhase.MovementParentStateReturned
                or RequestCqrsProbePhase.WaitForIndexReadCutCaptured or RequestCqrsProbePhase.QueryReadCutCaptured or RequestCqrsProbePhase.ReplicaEntryObserverDisposed or RequestCqrsProbePhase.ProducerDisposed or RequestCqrsProbePhase.CanonicalOutboundObserved or RequestCqrsProbePhase.CanonicalIndependentAppendCompleted or RequestCqrsProbePhase.CanonicalOwnerDisposed
            || value.Phase == RequestCqrsProbePhase.TransferPageReturned
                && (value.ReadKind != KeyLoad.Clustering.GrainReadKind.PartitionMovementTransferData
                    || value.Action != RequestCqrsProbeAction.Hold)
            || value.Phase is (RequestCqrsProbePhase.ParentTransferCloseFailed
                or RequestCqrsProbePhase.ParentReceiverIssueAcknowledged or RequestCqrsProbePhase.ParentReceiverIssueObserved
                or RequestCqrsProbePhase.ParentFinalInstallPreflight)
                && (value.ReadKind is not null || value.CommandId == Guid.Empty || value.Action != RequestCqrsProbeAction.Hold)
            || value.Phase == RequestCqrsProbePhase.SampleChunkAdmissionRefused
                && (value.ReadKind is not null || value.CommandId == Guid.Empty || value.Action != RequestCqrsProbeAction.Hold)
            || value.Phase == RequestCqrsProbePhase.NativeTextOriginalPostingRead
                && (value.ReadKind != KeyLoad.Clustering.GrainReadKind.Search
                    || value.Action != RequestCqrsProbeAction.Hold || value.CommandId != Guid.Empty)
            || value.Phase == RequestCqrsProbePhase.DistributedSearchStatisticsCaptured
                && (value.ReadKind != KeyLoad.Clustering.GrainReadKind.DistributedSearch
                    || value.CommandId != Guid.Empty || value.Action != RequestCqrsProbeAction.Hold)
            || value.Phase == RequestCqrsProbePhase.OnlineTextCaptured
                && (value.ReadKind is not null || value.CommandId == Guid.Empty || value.Action != RequestCqrsProbeAction.Hold)
            || value.Phase == RequestCqrsProbePhase.ReplicaSnapshotReadiness
                && (value.ReadKind != KeyLoad.Clustering.GrainReadKind.NodeStatus
                    || value.Action != RequestCqrsProbeAction.Hold || value.CommandId != Guid.Empty)
            || value.Phase == RequestCqrsProbePhase.EventVectorFirstDispatchReserved
                && (value.ReadKind is not null || value.CommandId == Guid.Empty || value.Action != RequestCqrsProbeAction.Hold
                    || value.Partition is not null || value.SourceRequestId is not null || value.SourceArmId is not null
                    || value.TargetVoter is not null)
            || value.Phase is (RequestCqrsProbePhase.NativeModelBeforeDispatch or RequestCqrsProbePhase.NativeModelAfterDispatch or RequestCqrsProbePhase.NativeModelReplayBeforeDispatch)
                && !RequestCqrsLifetimeReadProbe.ValidArm(value)
            || !RequestCqrsCanonicalArmValidation.Valid(value)
            || value.TargetVoter is { } target && strictUtf8.GetByteCount(target) > executionOptions.Value.MaximumPrincipalBytes
            || read != (value.CommandId == Guid.Empty) || read && !Enum.IsDefined(value.ReadKind!.Value))
        {
            throw Invalid();
        }
    }

    private static bool ValidPrincipal(string value, IOptions<RequestProbeExecutionOptions> executionOptions, UTF8Encoding strictUtf8)
    {
        if (!value.StartsWith(RequestCqrsProbeProtocol.PrincipalPrefix, StringComparison.Ordinal))
        {
            return false;
        }
        try
        {
            return strictUtf8.GetByteCount(value) <= executionOptions.Value.MaximumPrincipalBytes;
        }
        catch (EncoderFallbackException)
        {
            return false;
        }
    }

    private static InvalidOperationException Invalid() => new(RequestCqrsProbeJson.InvalidRecord);
}
