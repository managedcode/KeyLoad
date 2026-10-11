using System.Collections.Immutable;
using System.Security.Cryptography;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorCleanupParentCommit
{
    private const int Version = 1;
    private const long NextPass = 1;
    private const string Invalid = "The original interrupted event vector parent requires recovery.";

    internal static EventVectorParentCall RequirePrevious(IKeyValueView view, EventVectorOwnedInventory inventory,
        PrincipalRecord principal, EventVectorControlPhase phase, IAuthorizationPolicy authorization, EventVectorAdmissionPolicy admission)
    {
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        var key = EventVectorKeys.ParentCall(map.ControlPartition, map.MapId);
        var row = inventory.PayloadRows.SingleOrDefault(current => current.Key.Span.SequenceEqual(key));
        if (row.Key.IsEmpty)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        var original = EventVectorParentValidation.Read(map, row, admission);
        if (original.PrincipalId != principal.Id || original.OriginalPublicCommandId == phase.OriginalRequest.CommandId
            || phase.OriginalRequest.Action != EventFeedControlAction.Release)
        { throw Errors.Fail(ErrorCode.Conflict, Invalid); }
        EventVectorParentScopes.Authorize(authorization, principal, original.OriginalAuthorizedSourceScopes, admission);
        var originalPhase = EventVectorParentValidation.ReadPhase(original, admission);
        if (originalPhase.OriginalRequest.Action is not (EventFeedControlAction.Open or EventFeedControlAction.Refresh))
        { throw Errors.Fail(ErrorCode.Conflict, Invalid); }
        EventVectorParentScopes.RequireExact(original.OriginalAuthorizedSourceScopes,
            EventVectorParentScopes.Derive(originalPhase, inventory, admission));
        var cleanupKey = EventVectorKeys.CleanupCall(map.ControlPartition, map.MapId);
        var cleanupRow = inventory.PayloadRows.SingleOrDefault(current => current.Key.Span.SequenceEqual(cleanupKey));
        if (cleanupRow.Key.IsEmpty)
        {
            _ = EventVectorCleanupFrontierValidation.Require(inventory, phase.OriginalCleanupFrontierBytes,
                principal, authorization, admission);
            return original;
        }
        var cleanup = EventVectorCleanupValidation.Read(map, cleanupRow, admission);
        var priorPhase = EventVectorCleanupValidation.ReadPhase(cleanup, principal, original, authorization, admission);
        if (cleanup.ReleasePublicCommandId != phase.OriginalRequest.CommandId)
        {
            var outcome = EventVectorParentOutcome.Require(view, principal, priorPhase,
                cleanup.LastAdmittedCleanupPhaseId, null, map.ControlIncarnation, admission);
            if (priorPhase.Action != EventVectorControlAction.CompleteRelease || outcome.Result.Error is not null
                || outcome.Result.Get<EventVectorControlResult>().Map is not { Released: false } settled
                || settled.CleanupGeneration != map.CleanupGeneration || map.PendingPhaseId is not null
                || map.CleanupGeneration != checked(cleanup.CleanupGeneration + NextPass)
                || phase.ExpectedCleanupGeneration != map.CleanupGeneration)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
            _ = EventVectorCleanupFrontierValidation.Require(inventory, phase.OriginalCleanupFrontierBytes,
                principal, authorization, admission);
            return original;
        }
        EventVectorCleanupValidation.RequireRequest(cleanup, phase.OriginalRequest, admission);
        if (cleanup.CleanupGeneration != phase.ExpectedCleanupGeneration
            || cleanup.ReleaseExpiresAt != phase.OriginalParentExpiresAt
            || !cleanup.AuthenticatedFrontierBytes.Span.SequenceEqual(phase.OriginalCleanupFrontierBytes.Span))
        { throw Errors.Fail(ErrorCode.Conflict, Invalid); }
        return original;
    }

    internal static bool MatchesInterruptedPending(IKeyValueView view, EventVectorOwnedInventory inventory,
        PrincipalRecord principal, EventVectorControlPhase phase, IAuthorizationPolicy authorization, EventVectorAdmissionPolicy admission)
    {
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        if (phase.OriginalCleanupFrontierBytes.IsEmpty || phase.Action != EventVectorControlAction.AdmitSource
            || map.State is not (EventVectorMapState.Opening or EventVectorMapState.Refreshing)
            || map.PendingPhaseId is not { } pending)
        { return false; }
        _ = RequirePrevious(view, inventory, principal, phase, authorization, admission);
        var frontier = EventVectorCleanupFrontierValidation.Require(inventory,
            phase.OriginalCleanupFrontierBytes, principal, authorization, admission);
        var key = EventVectorKeys.Phase(map.ControlPartition, map.MapId, pending);
        var row = inventory.PayloadRows.SingleOrDefault(current => current.Key.Span.SequenceEqual(key));
        if (row.Key.IsEmpty)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        return frontier.Any(pin => pin.OriginalPinPhaseBytes.Span.SequenceEqual(row.Value.Span)
            || pin.LastAdmittedAdvancePhaseBytes.Span.SequenceEqual(row.Value.Span));
    }

    internal static void Prepare(IAtomicTransaction transaction, PrincipalRecord principal,
        EventVectorControlPhase phase, ReplicatedOperation operation, IAuthorizationPolicy authorization, IOptions<DatabaseLimits> options)
    {
        var admission = new EventVectorAdmissionPolicy(options);
        var request = phase.OriginalRequest;
        var inventory = EventVectorMapInventory.Read(transaction, request.ControlPartition, request.MapId,
            options, CancellationToken.None);
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        var key = EventVectorKeys.CleanupCall(map.ControlPartition, map.MapId);
        var prior = inventory.PayloadRows.SingleOrDefault(row => row.Key.Span.SequenceEqual(key));
        if (!prior.Key.IsEmpty)
        {
            var cleanup = EventVectorCleanupValidation.Read(map, prior, admission);
            if (cleanup.ReleasePublicCommandId != request.CommandId)
            { Write(transaction, principal, phase, operation, authorization, options); }
            return;
        }
        _ = RequirePrevious(transaction, inventory, principal, phase, authorization, admission);
        Write(transaction, principal, phase, operation, authorization, options);
        inventory = EventVectorMapInventory.Read(transaction, request.ControlPartition, request.MapId,
            options, CancellationToken.None);
        // The original pending phase remains immutable in its row and original parent/cleanup frontier.
        // The active pointer now belongs to the newly admitted cleanup pass; no source disposition is inferred.
        _ = EventVectorMapWriter.Write(transaction, map with
        { State = EventVectorMapState.Releasing, PendingPhaseId = null }, inventory.PayloadRows,
            options, CancellationToken.None);
    }

    internal static void Write(IAtomicTransaction transaction, PrincipalRecord principal,
        EventVectorControlPhase phase, ReplicatedOperation operation, IAuthorizationPolicy authorization, IOptions<DatabaseLimits> options)
    {
        var admission = new EventVectorAdmissionPolicy(options);
        var request = phase.OriginalRequest;
        var inventory = EventVectorMapInventory.Read(transaction, request.ControlPartition, request.MapId,
            options, CancellationToken.None);
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        var original = RequirePrevious(transaction, inventory, principal, phase, authorization, admission);
        var key = EventVectorKeys.CleanupCall(map.ControlPartition, map.MapId);
        var prior = inventory.PayloadRows.SingleOrDefault(row => row.Key.Span.SequenceEqual(key));
        var raw = NativeSerialization.Deserialize<NativeCommandPayload>(operation.NativePayload.Span).Value;
        admission.RequireEncodedBytes(raw.Length);
        var scopes = EventVectorParentScopes.Derive(phase, inventory, admission);
        EventVectorParentScopes.Authorize(authorization, principal, scopes, admission);
        EventVectorCleanupCall cleanup;
        if (prior.Key.IsEmpty || EventVectorCleanupValidation.Read(map, prior, admission).ReleasePublicCommandId
            != request.CommandId)
        {
            admission.RequireEncodedBytes(NativeSerialization.Measure(request));
            var requestBytes = NativeSerialization.Serialize(request);
            admission.RequireEncodedBytes(requestBytes.Length);
            cleanup = new(Version, map.MapId, map.ControlPartition, original.OriginalPublicCommandId,
                original.OriginalPublicRequestDigest.ToArray(), original.OriginalAdmittedControlPhaseBytes.ToArray(),
                requestBytes, request.CommandId, EventVectorControlBodyProjection.RequestDigest(request, admission),
                phase.OriginalParentExpiresAt, phase.OriginalCleanupFrontierBytes.ToArray(),
                SHA256.HashData(phase.OriginalCleanupFrontierBytes.Span), operation.Id, raw.ToArray(),
                scopes, phase.ExpectedCleanupGeneration);
        }
        else
        {
            cleanup = EventVectorCleanupValidation.Read(map, prior, admission);
            EventVectorParentScopes.RequireExact(cleanup.OriginalAuthorizedSourceScopes, scopes);
            cleanup = cleanup with
            {
                LastAdmittedCleanupPhaseId = operation.Id,
                LastAdmittedCleanupPhaseBytes = raw.ToArray()
            };
        }
        var payload = inventory.PayloadRows.Where(row => !row.Key.Span.SequenceEqual(key)).ToImmutableArray()
            .Add(EventVectorEncodedRow.Create(key, cleanup, admission));
        _ = EventVectorMapWriter.Write(transaction, map, payload, options, CancellationToken.None);
    }
}
