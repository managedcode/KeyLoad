using System.Security.Cryptography;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorFirstIssuanceValidation
{
    private const int Version = 1;
    private const string Invalid = "The original event vector first issuance requires recovery.";

    internal static EventVectorFirstIssuance Read(IKeyValueView view, PartitionRef partition,
        string principalId, Guid publicCommandId, bool terminal, EventVectorAdmissionPolicy admission)
    {
        ArgumentNullException.ThrowIfNull(view);
        ArgumentNullException.ThrowIfNull(partition);
        ArgumentNullException.ThrowIfNull(admission);
        var key = terminal ? EventVectorKeys.TerminalFirstIssuance(partition, principalId, publicCommandId)
            : EventVectorKeys.FirstIssuance(partition, principalId, publicCommandId);
        var row = EventVectorRowStorage.Read<EventVectorFirstIssuance>(view, key, admission)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        if (row.Version != Version || row.PrincipalId != principalId || row.ControlPartition != partition
            || row.OriginalPublicCommandId != publicCommandId || row.MapId == Guid.Empty
            || row.OriginalParentRequestId == Guid.Empty
            || row.OriginalParentExpiresAt == default || row.OriginalPublicRequestBytes.IsEmpty
            || row.OriginalCoverageWitnessBytes.IsEmpty || row.OriginalAuthorizedSourceScopes.IsDefault
            || row.OriginalPublicRequestDigest.Length != SHA256.HashSizeInBytes || !Enum.IsDefined(row.State)
            || terminal != (row.State == EventVectorFirstIssuanceState.Released))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        admission.RequireEntryCount(row.OriginalAuthorizedSourceScopes.Length);
        RequireCleanup(row);
        return row;
    }

    internal static EventFeedControlRequest RequireRequest(EventVectorFirstIssuance row,
        EventFeedControlRequest requested, EventVectorAdmissionPolicy admission)
    {
        var original = NativeSerialization.Deserialize<EventFeedControlRequest>(row.OriginalPublicRequestBytes.Span);
        if (original.Action != EventFeedControlAction.Open || original.CommandId != row.OriginalPublicCommandId
            || original.ControlPartition != row.ControlPartition || original.MapId != row.MapId
            || requested.Action != EventFeedControlAction.Open || requested.CommandId != row.OriginalPublicCommandId
            || requested.ControlPartition != row.ControlPartition || requested.MapId != row.MapId
            || !CryptographicOperations.FixedTimeEquals(
                EventVectorControlBodyProjection.RequestDigest(original, admission).Span,
                row.OriginalPublicRequestDigest.Span)
            || !CryptographicOperations.FixedTimeEquals(
                EventVectorControlBodyProjection.RequestDigest(requested, admission).Span,
                row.OriginalPublicRequestDigest.Span))
        { throw Errors.Fail(ErrorCode.Conflict, Invalid); }
        return original;
    }

    private static void RequireCleanup(EventVectorFirstIssuance row)
    {
        var released = row.State == EventVectorFirstIssuanceState.Released;
        if (released != (row.LastCleanupNativeCommandId != Guid.Empty)
            || released != !row.LastCleanupNativePayloadBytes.IsEmpty
            || released != !string.IsNullOrWhiteSpace(row.LastCleanupNativeFingerprint)
            || row.State == EventVectorFirstIssuanceState.Admitted
                && !row.OriginalCapturedFirstOpenPhaseBytes.IsEmpty
            || row.State is EventVectorFirstIssuanceState.Captured or EventVectorFirstIssuanceState.DispatchReserved
                && row.OriginalCapturedFirstOpenPhaseBytes.IsEmpty)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
    }
}
