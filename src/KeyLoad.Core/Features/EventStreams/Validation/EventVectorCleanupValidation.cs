using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorCleanupValidation
{
    private const int Version = 1;
    private const long InitialGeneration = 0;
    private const string Invalid = "The original event vector cleanup owner requires recovery.";

    internal static EventVectorCleanupCall Read(EventVectorMap map, EventVectorEncodedRow row,
        EventVectorAdmissionPolicy admission)
    {
        admission.RequireEncodedBytes(row.EncodedBytes);
        var call = NativeSerialization.Deserialize<EventVectorCleanupCall>(row.Value.Span);
        if (call.Version != Version || call.MapId != map.MapId || call.ControlPartition != map.ControlPartition
            || call.OriginalParentCommandId == Guid.Empty || call.ReleasePublicCommandId == Guid.Empty
            || call.ReleasePublicCommandId == call.OriginalParentCommandId || call.ReleaseExpiresAt == default
            || call.LastAdmittedCleanupPhaseId == Guid.Empty || call.CleanupGeneration < InitialGeneration
            || call.OriginalAuthorizedSourceScopes.IsDefault || call.OriginalAdmittedPhaseBytes.IsEmpty
            || call.OriginalReleaseRequestBytes.IsEmpty || call.AuthenticatedFrontierBytes.IsEmpty
            || call.LastAdmittedCleanupPhaseBytes.IsEmpty
            || call.OriginalParentRequestDigest.Length != SHA256.HashSizeInBytes
            || call.ReleaseRequestDigest.Length != SHA256.HashSizeInBytes
            || call.FrontierChecksum.Length != SHA256.HashSizeInBytes
            || !row.Key.Span.SequenceEqual(EventVectorKeys.CleanupCall(map.ControlPartition, map.MapId)))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        admission.RequireEntryCount(call.OriginalAuthorizedSourceScopes.Length);
        var request = NativeSerialization.Deserialize<EventFeedControlRequest>(call.OriginalReleaseRequestBytes.Span);
        RequireRequest(call, request, admission);
        if (!CryptographicOperations.FixedTimeEquals(SHA256.HashData(call.AuthenticatedFrontierBytes.Span),
            call.FrontierChecksum.Span))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        return call;
    }

    internal static void RequireRequest(EventVectorCleanupCall call, EventFeedControlRequest request,
        EventVectorAdmissionPolicy admission)
    {
        if (request.Action != EventFeedControlAction.Release || request.CommandId != call.ReleasePublicCommandId
            || request.MapId != call.MapId || request.ControlPartition != call.ControlPartition
            || !CryptographicOperations.FixedTimeEquals(EventVectorControlBodyProjection.RequestDigest(request,
                admission).Span, call.ReleaseRequestDigest.Span))
        { throw Errors.Fail(ErrorCode.Conflict, Invalid); }
    }

    internal static EventVectorControlPhase ReadPhase(EventVectorCleanupCall call, PrincipalRecord principal,
        EventVectorParentCall originalParent, IAuthorizationPolicy authorization, EventVectorAdmissionPolicy admission)
    {
        EventVectorParentScopes.Authorize(authorization, principal, call.OriginalAuthorizedSourceScopes, admission);
        if (originalParent.OriginalPublicCommandId != call.OriginalParentCommandId
            || !CryptographicOperations.FixedTimeEquals(originalParent.OriginalPublicRequestDigest.Span,
                call.OriginalParentRequestDigest.Span)
            || !originalParent.OriginalAdmittedControlPhaseBytes.Span.SequenceEqual(call.OriginalAdmittedPhaseBytes.Span))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        var phase = NativeSerialization.Deserialize<EventVectorControlPhase>(call.LastAdmittedCleanupPhaseBytes.Span);
        RequireRequest(call, phase.OriginalRequest, admission);
        if (phase.OriginalParentExpiresAt != call.ReleaseExpiresAt
            || phase.ExpectedCleanupGeneration != call.CleanupGeneration
            || EventVectorControlPhaseIds.For(phase, principal.Id,
                EventVectorControlPhaseIds.Project(phase, principal.Id, admission).OriginalBodyDigest, admission)
                != call.LastAdmittedCleanupPhaseId)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        return phase;
    }
}
