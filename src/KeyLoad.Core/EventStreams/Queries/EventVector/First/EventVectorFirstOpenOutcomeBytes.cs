using System.Collections.Immutable;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorFirstOpenOutcomeBytes
{
    private const string Invalid = "The original event vector first Open outcome requires recovery.";

    internal static ReadOnlyMemory<byte> Read(IKeyValueView view, PrincipalRecord principal,
        ReadOnlyMemory<byte> originalPhaseBytes, Guid originalCommandId, Guid incarnation,
        ImmutableArray<EventVectorAuthorizationScope> originalScopes,
        IAuthorizationPolicy authorization, EventVectorAdmissionPolicy admission)
    {
        EventVectorFirstOpenProof.RequirePair(originalPhaseBytes, originalCommandId);
        if (originalPhaseBytes.IsEmpty)
        {
            return ReadOnlyMemory<byte>.Empty;
        }
        EventVectorParentScopes.Authorize(authorization, principal, originalScopes, admission);
        admission.RequireEncodedBytes(originalPhaseBytes.Length);
        var phase = EventVectorFirstPhaseCarrier.Read(originalPhaseBytes, admission);
        var scopes = EventVectorParentScopes.Derive(phase, null, admission);
        EventVectorParentScopes.RequireExact(originalScopes, scopes);
        var actualId = EventVectorControlPhaseIds.For(phase, principal.Id,
            EventVectorControlBodyProjection.Digest(phase, admission), admission);
        if (actualId != originalCommandId)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        var selected = CommandOutcomeKeyResolver.Select(view, principal.Id, originalCommandId,
            new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition,
                phase.OriginalRequest.ControlPartition));
        if (selected.Outcome is null)
        {
            return ReadOnlyMemory<byte>.Empty;
        }
        _ = EventVectorParentOutcome.Require(view, principal, phase, originalCommandId,
            null, incarnation, admission);
        var raw = EventVectorFirstLookupRows.Read(view, selected.Key, admission);
        if (raw.IsEmpty)
        {
            throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid);
        }
        return raw;
    }
}

