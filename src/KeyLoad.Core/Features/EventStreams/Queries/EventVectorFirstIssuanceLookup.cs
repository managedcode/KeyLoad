using System.Collections.Immutable;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorFirstIssuanceLookup
{
    private const int Version = 1;
    private const string Invalid = "The original event vector first issuance lookup requires recovery.";

    internal static EventVectorFirstIssuanceObservation Read(IKeyValueView boundedView,
        PrincipalRecord principal, EventFeedControlRequest request, PhysicalShardRecord owner,
        StoreIdentity identity, IAuthorizationPolicy authorization, IOptions<DatabaseLimits> options,
        CancellationToken cancellationToken)
    {
        var admission = new EventVectorAdmissionPolicy(options);
        var admit = EventVectorFirstAdmitOutcome.Observe(boundedView, principal, request,
            identity.Incarnation, authorization, admission);
        var active = EventVectorFirstLookupRows.Active(boundedView, principal, request, admission);
        var terminal = EventVectorFirstLookupRows.Terminal(boundedView, principal, request, admission);
        var map = EventVectorFirstLookupRows.Read(boundedView,
            EventVectorKeys.Map(request.ControlPartition, request.MapId), admission);
        var parent = EventVectorFirstLookupRows.Parent(boundedView, request, admission);
        var index = EventVectorFirstLookupRows.Index(boundedView, principal, request, admission);
        var capacity = EventVectorFirstLookupRows.Read(boundedView,
            EventVectorKeys.Capacity(request.ControlPartition), admission);
        if (admit.Outcome is null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        if (admit.Outcome.Result.Error is not null)
        {
            if (!active.IsEmpty || !terminal.IsEmpty || !map.IsEmpty || !parent.IsEmpty || !index.IsEmpty)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
            return new(Version, principal.Id, request.CommandId, owner, identity.NodeId, identity.ReadGeneration,
                admit.CommandId, admit.Raw, active, map, parent, index, ReadOnlyMemory<byte>.Empty,
                terminal, ReadOnlyMemory<byte>.Empty, capacity);
        }
        if (!active.IsEmpty && !terminal.IsEmpty || !map.IsEmpty && (!active.IsEmpty || !terminal.IsEmpty)
            || active.IsEmpty && terminal.IsEmpty && map.IsEmpty && index.IsEmpty)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        cancellationToken.ThrowIfCancellationRequested();
        var proof = map.IsEmpty && index.IsEmpty
            ? IssuanceProof(boundedView, principal, request, !terminal.IsEmpty, identity.Incarnation, authorization, admission)
            : PublishedProof(boundedView, principal, request, identity.Incarnation,
                authorization, options, cancellationToken);
        var originalOutcome = EventVectorFirstOpenOutcomeBytes.Read(boundedView, principal, proof.Phase,
            proof.CommandId, identity.Incarnation, proof.Scopes, authorization, admission);
        EventVectorParentScopes.Authorize(authorization, principal, proof.Scopes, admission);
        parent = OriginalParent(parent, request.CommandId);
        if ((!map.IsEmpty || !index.IsEmpty) && originalOutcome.IsEmpty)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        return new(Version, principal.Id, request.CommandId, owner, identity.NodeId, identity.ReadGeneration,
            admit.CommandId, admit.Raw, proof.Abandoned ? ReadOnlyMemory<byte>.Empty : active, map, parent, index,
            proof.Abandoned ? active : ReadOnlyMemory<byte>.Empty, terminal, originalOutcome, capacity);
    }

    private static (ReadOnlyMemory<byte> Phase, Guid CommandId,
        ImmutableArray<EventVectorAuthorizationScope> Scopes, bool Abandoned) IssuanceProof(
        IKeyValueView view, PrincipalRecord principal, EventFeedControlRequest request, bool terminal, Guid incarnation,
        IAuthorizationPolicy authorization, EventVectorAdmissionPolicy admission)
    {
        var row = EventVectorFirstIssuanceValidation.Read(view, request.ControlPartition,
            principal.Id, request.CommandId, terminal, admission);
        EventVectorParentScopes.Authorize(authorization, principal, row.OriginalAuthorizedSourceScopes, admission);
        _ = EventVectorFirstIssuanceValidation.RequireRequest(row, request, admission);
        if (terminal)
        { EventVectorFirstTerminalOutcome.Require(view, principal, row, incarnation, admission); }
        var commandId = Guid.Empty;
        if (!row.OriginalCapturedFirstOpenPhaseBytes.IsEmpty)
        {
            var first = EventVectorFirstPhaseCarrier.Read(row.OriginalCapturedFirstOpenPhaseBytes, admission);
            EventVectorParentScopes.RequireExact(row.OriginalAuthorizedSourceScopes,
                EventVectorParentScopes.Derive(first, null, admission));
            commandId = EventVectorControlPhaseIds.For(first, principal.Id,
                EventVectorControlBodyProjection.Digest(first, admission), admission);
        }
        return (row.OriginalCapturedFirstOpenPhaseBytes, commandId, row.OriginalAuthorizedSourceScopes,
            row.State == EventVectorFirstIssuanceState.Abandoned);
    }

    private static (ReadOnlyMemory<byte> Phase, Guid CommandId,
        ImmutableArray<EventVectorAuthorizationScope> Scopes, bool Abandoned) PublishedProof(
        IKeyValueView view, PrincipalRecord principal, EventFeedControlRequest request, Guid incarnation,
        IAuthorizationPolicy authorization, IOptions<DatabaseLimits> options, CancellationToken cancellationToken)
    {
        var admission = new EventVectorAdmissionPolicy(options);
        var indexKey = EventVectorKeys.ParentOutcome(request.ControlPartition, principal.Id, request.CommandId);
        var index = EventVectorRowStorage.Read<EventVectorParentOutcomeIndex>(view, indexKey, admission);
        if (index is not null)
        {
            _ = EventVectorParentLookup.ReadTerminalDetails(view, authorization, principal, request,
                incarnation, options, cancellationToken) ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
            RequirePublishedPair(index.OriginalFirstOpenPhaseBytes, index.OriginalFirstOpenNativeCommandId);
            return (index.OriginalFirstOpenPhaseBytes, index.OriginalFirstOpenNativeCommandId,
                index.OriginalAuthorizedSourceScopes, false);
        }
        var inventory = EventVectorMapInventory.Read(view, request.ControlPartition, request.MapId,
            options, cancellationToken);
        var map = inventory.Header ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        if (map.PrincipalId != principal.Id || map.ControlPolicyEpoch != principal.PolicyEpoch
            || map.ControlIncarnation != incarnation || map.OriginalOpenCommandId != request.CommandId)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, Invalid); }
        EventVectorParentInventoryAuthorization.Require(authorization, principal, inventory, admission);
        EventVectorInventoryValidation.Require(map, inventory.PayloadRows, admission);
        _ = EventVectorParentObservationRead.Read(view, authorization, principal, request,
            incarnation, options, cancellationToken);
        var call = EventVectorRowStorage.Read<EventVectorParentCall>(view,
            EventVectorKeys.ParentCall(request.ControlPartition, request.MapId), admission)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        RequirePublishedPair(call.OriginalFirstOpenPhaseBytes, call.OriginalFirstOpenNativeCommandId);
        return (call.OriginalFirstOpenPhaseBytes, call.OriginalFirstOpenNativeCommandId,
            call.OriginalAuthorizedSourceScopes, false);
    }
    private static ReadOnlyMemory<byte> OriginalParent(ReadOnlyMemory<byte> bytes, Guid publicCommandId)
    {
        if (bytes.IsEmpty)
        { return bytes; }
        var parent = NativeSerialization.Deserialize<EventVectorParentCall>(bytes.Span);
        return parent.OriginalPublicCommandId == publicCommandId ? bytes : ReadOnlyMemory<byte>.Empty;
    }

    private static void RequirePublishedPair(ReadOnlyMemory<byte> phase, Guid commandId)
    {
        if (phase.IsEmpty || commandId == Guid.Empty)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
    }
}
