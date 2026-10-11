using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.InternalSerialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class EventVectorControlReplay
{
    private const string InvalidReplay = "The original native event vector control outcome is inconsistent.";

    internal static bool Read(IKeyValueView view, PrincipalRecord principal, EventVectorControlPhase phase,
        EventVectorAdmissionPolicy admission, Guid incarnation) => ReadOutcome(view, principal, phase, admission, incarnation) is not null;

    internal static OperationResult? ReadOutcome(IKeyValueView view, PrincipalRecord principal, EventVectorControlPhase phase,
        EventVectorAdmissionPolicy admission, Guid incarnation)
    {
        var identity = EventVectorControlOperationIdentity.Create(phase, principal.Id, admission);
        var id = identity.CommandId;
        var scope = new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition,
            phase.OriginalRequest.ControlPartition);
        var outcome = CommandOutcomeKeyResolver.Select(view, principal.Id, id, scope).Outcome;
        if (outcome is null)
        { return null; }
        var metadata = identity.Metadata;
        var operation = new ReplicatedOperation(id, OperationKind.EventFeedControl, principal.Id,
            default, metadata);
        if (outcome.Incarnation != incarnation || outcome.PolicyEpoch != principal.PolicyEpoch
            || outcome.Fingerprint != NativeOperationFingerprint.Compute(operation))
        { throw Errors.Fail(ErrorCode.Corruption, InvalidReplay); }
        return outcome.Result;
    }
}
