using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Storage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Core;

internal static class EventVectorParentObservationRead
{
    private const string Invalid = "The original event vector parent observation requires recovery.";

    internal static (ReadOnlyMemory<byte> Phase, ReadOnlyMemory<byte> Outcome) Read(IKeyValueView view,
        IAuthorizationPolicy authorization, PrincipalRecord principal, EventFeedControlRequest request, Guid incarnation,
        IOptions<DatabaseLimits> options, CancellationToken cancellationToken)
    {
        var admission = new EventVectorAdmissionPolicy(options);
        var terminal = EventVectorParentLookup.ReadTerminalDetails(view, authorization, principal, request, incarnation,
            options, cancellationToken);
        if (terminal is { } actual)
        { return (actual.PhaseBytes, OutcomeBytes(view, principal, request, actual.PhaseId, admission)); }
        var budget = new EventVectorInventoryReadBudget(options);
        var key = EventVectorKeys.ParentCall(request.ControlPartition, request.MapId);
        EventVectorEncodedRow? row = null;
        view.ReadValue(key, bytes =>
        {
            admission.RequireEncodedBytes(checked(key.Length + bytes.Length));
            row = new(key, bytes.ToArray());
        }, budget.Observe);
        if (row is not { } actualRow)
        { return (ReadOnlyMemory<byte>.Empty, ReadOnlyMemory<byte>.Empty); }
        var map = EventVectorRowStorage.Read<EventVectorMap>(view,
            EventVectorKeys.Map(request.ControlPartition, request.MapId), admission)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        if (map.PrincipalId != principal.Id || map.ControlPolicyEpoch != principal.PolicyEpoch
            || map.ControlIncarnation != incarnation)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, Invalid); }
        var call = EventVectorParentValidation.Read(map, actualRow, admission);
        EventVectorParentValidation.RequireRequest(call, request, admission);
        EventVectorParentScopes.Authorize(authorization, principal, call.OriginalAuthorizedSourceScopes, admission);
        cancellationToken.ThrowIfCancellationRequested();
        var inventory = EventVectorMapInventory.Read(view, request.ControlPartition, request.MapId,
            options, cancellationToken);
        EventVectorInventoryValidation.Require(map, inventory.PayloadRows, admission);
        var phase = EventVectorParentValidation.ReadPhase(call, admission);
        EventVectorParentScopes.RequireExact(call.OriginalAuthorizedSourceScopes,
            EventVectorParentScopes.Derive(phase, inventory, admission));
        _ = EventVectorParentOutcome.Require(view, principal, phase, call.LastAdmittedControlPhaseId,
            null, incarnation, admission);
        return (call.OriginalAdmittedControlPhaseBytes,
            OutcomeBytes(view, principal, request, call.LastAdmittedControlPhaseId, admission));
    }

    private static ReadOnlyMemory<byte> OutcomeBytes(IKeyValueView view, PrincipalRecord principal,
        EventFeedControlRequest request, Guid phaseId, EventVectorAdmissionPolicy admission)
    {
        var selected = CommandOutcomeKeyResolver.Select(view, principal.Id, phaseId,
            new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, request.ControlPartition));
        if (selected.Outcome is null)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        var original = ReadOnlyMemory<byte>.Empty;
        var found = view.ReadValue(selected.Key, bytes =>
        {
            admission.RequireEncodedBytes(checked(selected.Key.Length + bytes.Length));
            original = bytes.ToArray();
        });
        if (!found || original.IsEmpty)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        return original;
    }
}
