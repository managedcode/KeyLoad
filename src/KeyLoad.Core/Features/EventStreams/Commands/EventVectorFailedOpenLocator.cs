using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorFailedOpenLocator
{
    private const int Version = 1;
    private const string Invalid = "The original event vector first Open outcome locator is inconsistent.";

    internal static EventVectorFailedOpenEligibility? Capture(IAtomicTransaction transaction,
        PrincipalRecord principal, ReplicatedOperation operation, Guid incarnation, IAuthorizationPolicy authorization, IOptions<DatabaseLimits> options)
    {
        if (operation.Kind != OperationKind.EventFeedControl)
        { return null; }
        var native = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span);
        var phase = NativeCommandPayload.Read<EventVectorControlPhase>(operation);
        if (phase.Action != EventVectorControlAction.Open)
        { return null; }
        var admission = new EventVectorAdmissionPolicy(options);
        var request = phase.OriginalRequest;
        if (EventVectorParentLookup.ReadTerminal(transaction, authorization, principal, request, incarnation, options,
            CancellationToken.None) is not null)
        { throw Errors.Fail(ErrorCode.Conflict, Invalid); }
        var scopes = EventVectorParentScopes.Derive(phase, null, admission);
        EventVectorParentScopes.Authorize(authorization, principal, scopes, admission);
        admission.RequireEncodedBytes(native.Value.Length);
        var digest = EventVectorControlBodyProjection.RequestDigest(request, admission);
        var index = new EventVectorParentOutcomeIndex(Version, principal.Id, request.CommandId,
            request.ControlPartition, request.MapId, digest, native.Value.ToArray(), operation.Id,
            NativeOperationFingerprint.Compute(operation))
        {
            OriginalAuthorizedSourceScopes = scopes,
            OriginalFirstOpenPhaseBytes = native.Value.ToArray(),
            OriginalFirstOpenNativeCommandId = operation.Id
        };
        // Bound the complete actual locator before allowing eligibility to survive Reset.
        _ = EventVectorEncodedRow.Create(EventVectorKeys.ParentOutcome(request.ControlPartition,
            principal.Id, request.CommandId), index, admission);
        return new(index, incarnation, principal.PolicyEpoch);
    }

    internal static void Write(IAtomicTransaction transaction, EventVectorFailedOpenEligibility? eligible,
        StoredOutcome actualOutcome, IOptions<DatabaseLimits> options)
    {
        if (eligible is null || actualOutcome.Result.Error is null)
        { return; }
        var index = eligible.Index;
        if (actualOutcome.Incarnation != eligible.Incarnation || actualOutcome.PolicyEpoch != eligible.PolicyEpoch
            || actualOutcome.Fingerprint != index.ActualTerminalOutcomeFingerprint)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        var admission = new EventVectorAdmissionPolicy(options);
        EventVectorRowStorage.Write(transaction, EventVectorEncodedRow.Create(EventVectorKeys.ParentOutcome(
            index.ControlPartition, index.PrincipalId, index.OriginalPublicCommandId), index, admission));
    }
}
