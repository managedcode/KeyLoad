using System.Security.Cryptography;

namespace KeyLoad.Core;

internal static class EventVectorParentValidation
{
    private const int Version = 1;
    private const string InvalidParent = "The original native event vector parent row is inconsistent.";

    internal static EventVectorParentCall Read(EventVectorMap map, EventVectorEncodedRow row,
        EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(admission);
        admission.RequireEncodedBytes(row.EncodedBytes);
        var call = NativeSerialization.Deserialize<EventVectorParentCall>(row.Value.Span);
        if (call.Version != Version || call.ControlPartition != map.ControlPartition || call.MapId != map.MapId
            || call.PrincipalId != map.PrincipalId || call.OriginalPublicCommandId == Guid.Empty
            || call.LastAdmittedControlPhaseId == Guid.Empty || call.OriginalPublicRequestBytes.IsEmpty
            || call.OriginalAdmittedControlPhaseBytes.IsEmpty
            || call.OriginalParentExpiresAt == default || call.OriginalAuthorizedSourceScopes.IsDefault
            || call.OriginalPublicRequestDigest.Length != SHA256.HashSizeInBytes
            || !row.Key.Span.SequenceEqual(EventVectorKeys.ParentCall(map.ControlPartition, map.MapId)))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidParent); }
        admission.RequireEncodedBytes(call.OriginalPublicRequestBytes.Length);
        admission.RequireEncodedBytes(call.OriginalAdmittedControlPhaseBytes.Length);
        var request = NativeSerialization.Deserialize<EventFeedControlRequest>(call.OriginalPublicRequestBytes.Span);
        RequireRequest(call, request, admission);
        return call;
    }

    internal static EventVectorControlPhase ReadPhase(EventVectorParentCall call,
        EventVectorAdmissionPolicy admission)
    {
        var phase = NativeSerialization.Deserialize<EventVectorControlPhase>(call.OriginalAdmittedControlPhaseBytes.Span);
        RequireRequest(call, phase.OriginalRequest, admission);
        var identity = EventVectorControlPhaseIds.Project(phase, call.PrincipalId, admission);
        if (phase.OriginalParentExpiresAt != call.OriginalParentExpiresAt
            || EventVectorControlPhaseIds.For(phase, call.PrincipalId, identity.OriginalBodyDigest, admission)
                != call.LastAdmittedControlPhaseId)
        { throw Errors.Fail(ErrorCode.Corruption, InvalidParent); }
        return phase;
    }

    internal static void RequireRequest(EventVectorParentCall call, EventFeedControlRequest request,
        EventVectorAdmissionPolicy admission)
    {
        if (request.CommandId != call.OriginalPublicCommandId || request.ControlPartition != call.ControlPartition
            || request.MapId != call.MapId || !CryptographicOperations.FixedTimeEquals(
                EventVectorControlBodyProjection.RequestDigest(request, admission).Span,
                call.OriginalPublicRequestDigest.Span))
        { throw Errors.Fail(ErrorCode.Conflict, InvalidParent); }
    }
}
