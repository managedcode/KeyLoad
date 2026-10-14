using System.Collections.Immutable;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorParentCommit
{
    private const int Version = 1;
    private const int NoPriorCall = 0;
    private const string InvalidParent = "The original event vector parent admission is inconsistent.";

    internal static void RequirePrevious(IKeyValueView transaction, PrincipalRecord principal,
        EventVectorControlPhase phase, Guid incarnation, IAuthorizationPolicy authorization, IOptions<DatabaseLimits> options)
    {
        var request = phase.OriginalRequest;
        if (EventVectorParentLookup.ReadTerminal(transaction, authorization, principal, request, incarnation, options,
            CancellationToken.None) is not null)
        {
            throw Errors.Fail(ErrorCode.Conflict, InvalidParent);
        }
        var admission = new EventVectorAdmissionPolicy(options);
        var inventory = EventVectorMapInventory.Read(transaction, request.ControlPartition, request.MapId,
            options, CancellationToken.None);
        if (inventory.Header is not { } map)
        {
            return;
        }
        if (!phase.OriginalCleanupFrontierBytes.IsEmpty)
        {
            var original = EventVectorCleanupParentCommit.RequirePrevious(transaction, inventory, principal, phase, authorization, admission);
            _ = EventVectorParentOutcome.Require(transaction, principal,
                EventVectorParentValidation.ReadPhase(original, admission), original.LastAdmittedControlPhaseId,
                null, incarnation, admission);
            var cleanupKey = EventVectorKeys.CleanupCall(map.ControlPartition, map.MapId);
            var cleanupRow = inventory.PayloadRows.SingleOrDefault(row => row.Key.Span.SequenceEqual(cleanupKey));
            if (!cleanupRow.Key.IsEmpty)
            {
                var cleanup = EventVectorCleanupValidation.Read(map, cleanupRow, admission);
                var prior = EventVectorCleanupValidation.ReadPhase(cleanup, principal, original, authorization, admission);
                _ = EventVectorParentOutcome.Require(transaction, principal, prior,
                    cleanup.LastAdmittedCleanupPhaseId, null, incarnation, admission);
            }
            return;
        }
        var key = EventVectorKeys.ParentCall(map.ControlPartition, map.MapId);
        foreach (var row in inventory.PayloadRows)
        {
            if (!row.Key.Span.SequenceEqual(key))
            {
                continue;
            }
            var call = EventVectorParentValidation.Read(map, row, admission);
            if (call.PrincipalId != principal.Id)
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, InvalidParent);
            }
            EventVectorParentValidation.RequireRequest(call, request, admission);
            EventVectorParentScopes.Authorize(authorization, principal, call.OriginalAuthorizedSourceScopes, admission);
            var original = EventVectorParentValidation.ReadPhase(call, admission);
            EventVectorParentScopes.RequireExact(call.OriginalAuthorizedSourceScopes,
                EventVectorParentScopes.Derive(original, inventory, admission));
            if (call.OriginalParentExpiresAt != phase.OriginalParentExpiresAt
                || original.OriginalParentRequestId != phase.OriginalParentRequestId)
            {
                throw Errors.Fail(ErrorCode.Conflict, InvalidParent);
            }
            _ = EventVectorParentOutcome.Require(transaction, principal, original, call.LastAdmittedControlPhaseId,
                null, incarnation, admission);
            return;
        }
    }

    internal static object Write(IAtomicTransaction transaction, PrincipalRecord principal,
        EventVectorControlPhase phase, ReplicatedOperation operation, object value,
        ImmutableArray<EventVectorAuthorizationScope> originalAuthorizedScopes,
        ReadOnlyMemory<byte> raw, (ReadOnlyMemory<byte> Bytes, Guid CommandId) actualFirst,
        IAuthorizationPolicy authorization, IOptions<DatabaseLimits> options)
    {
        var admission = new EventVectorAdmissionPolicy(options);
        EventVectorParentScopes.Authorize(authorization, principal, originalAuthorizedScopes, admission);
        admission.RequireEncodedBytes(raw.Length);
        var request = phase.OriginalRequest;
        var digest = EventVectorControlBodyProjection.RequestDigest(request, admission);
        var terminal = IsTerminal(phase.Action, value);
        var inventory = EventVectorMapInventory.Read(transaction, request.ControlPartition, request.MapId,
            options, CancellationToken.None);
        var first = OriginalFirst(inventory, phase, actualFirst);
        if (inventory.Header is not null && !phase.OriginalCleanupFrontierBytes.IsEmpty)
        {
            EventVectorCleanupParentCommit.Write(transaction, principal, phase, operation, authorization, options);
            if (value is EventVectorMap)
            {
                value = EventVectorMapInventory.Read(transaction, request.ControlPartition, request.MapId,
                options, CancellationToken.None).Header!;
            }
            if (!terminal)
            {
                return value;
            }
        }
        else if (inventory.Header is { } map)
        {
            var key = EventVectorKeys.ParentCall(map.ControlPartition, map.MapId);
            var payload = inventory.PayloadRows.Where(row => !row.Key.Span.SequenceEqual(key)).ToImmutableArray();
            if (!terminal)
            {
                var prior = inventory.PayloadRows.Where(row => row.Key.Span.SequenceEqual(key)).ToArray();
                var originalRequest = OriginalRequest(prior, request, admission);
                var call = new EventVectorParentCall(Version, principal.Id, request.CommandId,
                    request.ControlPartition, request.MapId, originalRequest, digest, operation.Id, raw.ToArray())
                {
                    OriginalAuthorizedSourceScopes = originalAuthorizedScopes,
                    OriginalParentExpiresAt = phase.OriginalParentExpiresAt,
                    OriginalFirstOpenPhaseBytes = first.Bytes.ToArray(),
                    OriginalFirstOpenNativeCommandId = first.CommandId
                };
                payload = payload.Add(EventVectorEncodedRow.Create(key, call, admission));
            }
            var changed = EventVectorMapWriter.Write(transaction, map, payload, options, CancellationToken.None);
            if (value is EventVectorMap)
            {
                value = changed;
            }
        }
        else if (!terminal)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, InvalidParent);
        }
        if (terminal)
        {
            WriteTerminal(transaction, principal, request, operation, originalAuthorizedScopes, raw, first, digest, admission);
        }
        return value;
    }

    private static void WriteTerminal(IAtomicTransaction transaction, PrincipalRecord principal,
        EventFeedControlRequest request, ReplicatedOperation operation,
        ImmutableArray<EventVectorAuthorizationScope> originalAuthorizedScopes, ReadOnlyMemory<byte> raw,
        (ReadOnlyMemory<byte> Bytes, Guid CommandId) first, ReadOnlyMemory<byte> digest,
        EventVectorAdmissionPolicy admission)
    {
        var index = new EventVectorParentOutcomeIndex(Version, principal.Id, request.CommandId,
            request.ControlPartition, request.MapId, digest, raw.ToArray(), operation.Id,
            NativeOperationFingerprint.Compute(operation))
        {
            OriginalAuthorizedSourceScopes = originalAuthorizedScopes,
            OriginalFirstOpenPhaseBytes = first.Bytes.ToArray(),
            OriginalFirstOpenNativeCommandId = first.CommandId
        };
        EventVectorRowStorage.Write(transaction, EventVectorEncodedRow.Create(
            EventVectorKeys.ParentOutcome(request.ControlPartition, principal.Id, request.CommandId), index, admission));
    }

    private static (ReadOnlyMemory<byte> Bytes, Guid CommandId) OriginalFirst(
        EventVectorOwnedInventory inventory, EventVectorControlPhase phase,
        (ReadOnlyMemory<byte> Bytes, Guid CommandId) actualFirst)
    {
        var key = EventVectorKeys.ParentCall(phase.OriginalRequest.ControlPartition, phase.OriginalRequest.MapId);
        var rows = inventory.PayloadRows.Where(row => row.Key.Span.SequenceEqual(key)).ToArray();
        var previous = rows.Length == NoPriorCall ? null
            : NativeSerialization.Deserialize<EventVectorParentCall>(rows.Single().Value.Span);
        if (previous is not null && previous.OriginalPublicCommandId != phase.OriginalRequest.CommandId
            && !phase.OriginalCleanupFrontierBytes.IsEmpty)
        {
            return (ReadOnlyMemory<byte>.Empty, Guid.Empty);
        }
        return EventVectorFirstOpenProof.Preserve(previous, phase, actualFirst);
    }

    private static ReadOnlyMemory<byte> OriginalRequest(EventVectorEncodedRow[] prior,
        EventFeedControlRequest request, EventVectorAdmissionPolicy admission)
    {
        if (prior.Length != NoPriorCall)
        {
            return NativeSerialization.Deserialize<EventVectorParentCall>(prior.Single().Value.Span).OriginalPublicRequestBytes;
        }
        admission.RequireEncodedBytes(NativeSerialization.Measure(request));
        var bytes = NativeSerialization.Serialize(request);
        admission.RequireEncodedBytes(bytes.Length);
        return bytes;
    }

    private static bool IsTerminal(EventVectorControlAction action, object value)
        => action is EventVectorControlAction.PublishOpen or EventVectorControlAction.Offer
            or EventVectorControlAction.PublishAcknowledgement or EventVectorControlAction.PublishRefresh
            || action == EventVectorControlAction.CompleteRelease && value is EventVectorMap;
}
