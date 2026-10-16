using System.Text.Json;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorFirstAdmitOutcome
{
    private const int Version = 1;
    private const long FirstPosition = 1;
    private const string Invalid = "The original event vector first intent outcome requires recovery.";

    internal static (Guid CommandId, StoredOutcome Outcome, ReadOnlyMemory<byte> Raw) Read(
        IKeyValueView boundedView, PrincipalRecord principal, EventFeedControlRequest request,
        Guid incarnation, IAuthorizationPolicy authorization, EventVectorAdmissionPolicy admission,
        EventVectorReadLifetimeBinding? lifetimeBinding = null)
    {
        var identity = EventVectorFirstIssuanceIds.Admit(request, principal.Id, admission);
        var commandId = EventVectorFirstIssuanceIds.For(identity, admission);
        var selection = CommandOutcomeKeyResolver.Select(boundedView, principal.Id, commandId,
            new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, request.ControlPartition));
        var outcome = selection.Outcome ?? throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        var original = new ReplicatedOperation(commandId, OperationKind.EventFeedControl,
            principal.Id, default, JsonSerializer.Serialize(identity, JsonDefaults.Options));
        if (outcome.Incarnation != incarnation || outcome.PolicyEpoch != principal.PolicyEpoch
            || outcome.Fingerprint != NativeOperationFingerprint.Compute(original))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, Invalid);
        }
        if (outcome.Result.Error is null)
        {
            RequireResult(outcome.Result.Get<EventVectorFirstIssuanceResult>(), principal, request,
            commandId, incarnation, authorization, admission, lifetimeBinding);
        }
        else if (outcome.Result.NativeValue is not null)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        var raw = EventVectorFirstLookupRows.Read(boundedView, selection.Key, admission);
        if (raw.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        return (commandId, outcome, raw);
    }

    internal static (Guid CommandId, StoredOutcome? Outcome, ReadOnlyMemory<byte> Raw) Observe(
        IKeyValueView boundedView, PrincipalRecord principal, EventFeedControlRequest request,
        Guid incarnation, IAuthorizationPolicy authorization, EventVectorAdmissionPolicy admission,
        EventVectorReadLifetimeBinding? lifetimeBinding = null)
    {
        var identity = EventVectorFirstIssuanceIds.Admit(request, principal.Id, admission);
        var commandId = EventVectorFirstIssuanceIds.For(identity, admission);
        var selection = CommandOutcomeKeyResolver.Select(boundedView, principal.Id, commandId,
            new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, request.ControlPartition));
        if (selection.Outcome is null)
        {
            return (commandId, null, ReadOnlyMemory<byte>.Empty);
        }
        var actual = Read(boundedView, principal, request, incarnation, authorization, admission, lifetimeBinding);
        return (actual.CommandId, actual.Outcome, actual.Raw);
    }

    private static void RequireResult(EventVectorFirstIssuanceResult result, PrincipalRecord principal,
        EventFeedControlRequest request, Guid commandId, Guid incarnation, IAuthorizationPolicy authorization,
        EventVectorAdmissionPolicy admission, EventVectorReadLifetimeBinding? lifetimeBinding)
    {
        if (result.Version != Version || result.NativeCommandId != commandId || result.Issuance is null
            || result.Receipt is null || result.Receipt.CommandId != commandId
            || result.Receipt.Token.Incarnation != incarnation || result.Receipt.Token.Position < FirstPosition
            || result.Receipt.Token.AtomicPartitionId != request.ControlPartition.AtomicPartitionId
            || result.Issuance.Version != Version || result.Issuance.PrincipalId != principal.Id
            || result.Issuance.ControlPartition != request.ControlPartition
            || result.Issuance.OriginalPublicCommandId != request.CommandId
            || result.Issuance.MapId != request.MapId
            || result.Issuance.State != EventVectorFirstIssuanceState.Admitted)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        if (lifetimeBinding is null)
        {
            EventVectorParentScopes.Authorize(authorization, principal,
                result.Issuance.OriginalAuthorizedSourceScopes, admission);
        }
        else
        {
            lifetimeBinding.Authorize(authorization, principal, result.Issuance.OriginalAuthorizedSourceScopes, admission);
        }
        _ = EventVectorFirstIssuanceValidation.RequireRequest(result.Issuance, request, admission);
    }
}

