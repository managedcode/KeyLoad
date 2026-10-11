using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Replication;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.EventStreams;

internal static class EventVectorFirstOpenExecution
{
    private const int Version = 1;
    private const long InitialRevision = 0;
    private const string Invalid = "The original event vector first Open requires recovery.";

    internal static async Task<EventVectorControlResult> ExecuteAfterFreshAdmitAsync(OrleansNode node, RemoteDocumentRuntime runtime,
        IOptions<NodeOptions> options, IOptions<DatabaseLimits> limits,
        PartitionHost partition, TimeProvider clock, DecodedGrainRequest parent,
        GrainRequestCodec codec, global::Orleans.Runtime.IGrainContext context, EventVectorControlPhase originalAdmit, EventVectorFirstAdmitAppendResult actualAppend,
        EventVectorAdmissionPolicy admission,
        ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        originalCancellationToken.ThrowIfCancellationRequested();
        var admitted = RequireActualAdmit(parent, originalAdmit, actualAppend, admission);
        var originalPrincipalId = parent.Envelope.PrincipalId
            ?? throw Errors.Fail(ErrorCode.Unauthenticated, Invalid);
        using var prepared = partition.Database.PrepareVerifiedEventVectorFirstOpen(originalPrincipalId,
            originalAdmit, work, originalCancellationToken);
        var originalOpen = prepared.Phase;
        EventVectorControlFields.Require(originalOpen, admission);
        if (originalOpen.Action != EventVectorControlAction.Open
            || !originalOpen.OriginalCapturedFirstOpenPhaseBytes.IsEmpty
            || originalOpen.OriginalFirstIssuancePublicCommandId != Guid.Empty)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        var measuredBytes = NativeSerialization.Measure(originalOpen);
        admission.RequireEncodedBytes(measuredBytes);
        work.ChargeBytes(measuredBytes);
        var firstBytes = NativeSerialization.Serialize(originalOpen);
        try
        {
            admission.RequireEncodedBytes(firstBytes.Length);
            EventVectorFirstOpenStageResult.Require(admitted, originalAdmit,
                EventVectorFirstIssuanceState.Admitted, ReadOnlyMemory<byte>.Empty, originalPrincipalId,
                partition.Database.Store.Identity.Incarnation, admission);
            var capture = Stage(originalOpen, EventVectorControlAction.CaptureFirstPhase, firstBytes);
            var captured = await ExecuteStageAsync(node, partition, clock, parent, capture,
                admission, work, originalCancellationToken).ConfigureAwait(false);
            EventVectorFirstOpenStageResult.Require(captured, capture,
                EventVectorFirstIssuanceState.Captured, firstBytes, originalPrincipalId,
                partition.Database.Store.Identity.Incarnation, admission);
            var reserve = Stage(originalOpen, EventVectorControlAction.ReserveFirstDispatch, firstBytes);
            var reserved = await ExecuteStageAsync(node, partition, clock, parent, reserve,
                admission, work, originalCancellationToken).ConfigureAwait(false);
            EventVectorFirstOpenStageResult.Require(reserved, reserve,
                EventVectorFirstIssuanceState.DispatchReserved, firstBytes, originalPrincipalId,
                partition.Database.Store.Identity.Incarnation, admission);
            await codec.ObservePhaseAsync(parent, GrainRequestPhase.EventVectorFirstDispatchReserved,
                context, originalCancellationToken).ConfigureAwait(false);
            var result = await EventVectorParentControlCall.ExecuteAsync(node, partition, clock,
                parent, originalOpen, work, originalCancellationToken).ConfigureAwait(false);
            work.Check();
            if (result is not EventVectorControlResult actual)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
            return await EventVectorFirstOpenPins.CompleteAsync(runtime, node, partition, options.Value, clock,
                parent, originalOpen, actual, admission, new EventVectorRemoteEncoding(limits),
                work, originalCancellationToken).ConfigureAwait(false);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(firstBytes);
        }
    }

    private static EventVectorFirstIssuanceResult RequireActualAdmit(DecodedGrainRequest parent,
        EventVectorControlPhase originalAdmit, EventVectorFirstAdmitAppendResult actualAppend,
        EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(originalAdmit);
        ArgumentNullException.ThrowIfNull(actualAppend);
        ArgumentNullException.ThrowIfNull(admission);
        if (originalAdmit.Action != EventVectorControlAction.AdmitFirstIntent
            || parent.Envelope.Purpose != EventVectorRequestScope.ParentPurpose
            || parent.Envelope.CommandKind != OperationKind.EventFeedControl || parent.Envelope.ReadKind is not null
            || originalAdmit.OriginalRequest != GrainNativePayload.ReadCommand<EventFeedControlRequest>(parent.Payload)
            || originalAdmit.OriginalParentRequestId != parent.Envelope.RequestId
            || originalAdmit.OriginalParentExpiresAt != parent.Envelope.ExpiresAt)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        EventVectorControlFields.Require(originalAdmit, admission);
        return EventVectorFirstAppendValidation.Require(actualAppend, originalAdmit, admission);
    }

    private static EventVectorControlPhase Stage(EventVectorControlPhase original,
        EventVectorControlAction action, ReadOnlyMemory<byte> firstBytes)
        => new(Version, original.OriginalRequest, action, InitialRevision, InitialRevision,
            InitialRevision, null, null, null, ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty)
        {
            OriginalCoverageWitness = original.OriginalCoverageWitness,
            OriginalParentExpiresAt = original.OriginalParentExpiresAt,
            OriginalParentRequestId = original.OriginalParentRequestId,
            OriginalCapturedFirstOpenPhaseBytes = firstBytes
        };

    private static async Task<EventVectorFirstIssuanceResult> ExecuteStageAsync(OrleansNode node,
        PartitionHost partition, TimeProvider clock, DecodedGrainRequest parent,
        EventVectorControlPhase phase, EventVectorAdmissionPolicy admission,
        ReadExecutionBudget work, CancellationToken originalCancellationToken)
    {
        EventVectorControlFields.Require(phase, admission);
        var result = await EventVectorParentControlCall.ExecuteAsync(node, partition, clock,
            parent, phase, work, originalCancellationToken).ConfigureAwait(false);
        work.Check();
        return result is EventVectorFirstIssuanceResult actual ? actual
            : throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
    }
}
