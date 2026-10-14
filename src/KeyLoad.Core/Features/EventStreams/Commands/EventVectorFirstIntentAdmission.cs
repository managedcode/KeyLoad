using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorFirstIntentAdmission
{
    private const int Version = 1;
    private const string Invalid = "The original event vector first intent cannot be admitted.";

    internal static EventVectorFirstIssuance Execute(IAtomicTransaction transaction,
        PrincipalRecord principal, EventVectorControlPhase phase, ReplicatedOperation operation,
        IAuthorizationPolicy authorization, IOptions<DatabaseLimits> options)
    {
        ArgumentNullException.ThrowIfNull(transaction);
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentNullException.ThrowIfNull(phase);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentNullException.ThrowIfNull(options);
        var admission = new EventVectorAdmissionPolicy(options);
        var request = phase.OriginalRequest;
        if (phase.Action != EventVectorControlAction.AdmitFirstIntent
            || request.Action != EventFeedControlAction.Open
            || phase.OriginalParentExpiresAt <= operation.EvaluatedAt
            || phase.OriginalParentRequestId == Guid.Empty)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, Invalid);
        }
        var scopes = EventVectorParentScopes.Derive(phase, null, admission);
        EventVectorParentScopes.Authorize(authorization, principal, scopes, admission);
        var existing = EventVectorMapInventory.Read(transaction, request.ControlPartition,
            request.MapId, options, CancellationToken.None);
        if (existing.Header is not null
            || EventVectorRowStorage.Read<EventVectorParentOutcomeIndex>(transaction,
                EventVectorKeys.ParentOutcome(request.ControlPartition, principal.Id, request.CommandId),
                admission) is not null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        admission.RequireEncodedBytes(NativeSerialization.Measure(request));
        var rawRequest = NativeSerialization.Serialize(request);
        admission.RequireEncodedBytes(rawRequest.Length);
        var row = new EventVectorFirstIssuance(Version, principal.Id, request.CommandId,
            request.ControlPartition, request.MapId, rawRequest,
            EventVectorControlBodyProjection.RequestDigest(request, admission),
            phase.OriginalParentExpiresAt, scopes, phase.OriginalCoverageWitness.ToArray(),
            ReadOnlyMemory<byte>.Empty, EventVectorFirstIssuanceState.Admitted,
            Guid.Empty, ReadOnlyMemory<byte>.Empty, null)
        {
            OriginalParentRequestId = phase.OriginalParentRequestId
        };
        EventVectorFirstIssuanceWriter.Create(transaction, row, admission);
        return row;
    }
}

