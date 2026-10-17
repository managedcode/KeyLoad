using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void RequireMoveReceiverCaptureAdmission(IKeyValueView view, PrincipalRecord principal,
        PartitionMovePeerEnvelope original, PartitionMoveReceiverEffectAdmission? admission)
    {
        var grant = original.Grant
            ?? throw Errors.Fail(ErrorCode.OwnershipLost, PartitionMoveProtocol.MissingAuthority);
        var command = new PartitionMovePhaseCommand(original.Version, original.MoveId, original.Partition,
            original.ControlOwner, original.SourcePlacement, original.DestinationOwner, original.ControlIntentDigest,
            original.Stage, original.PageOrdinal, original.Body, grant.GrantId, grant.Resources, admission);
        RequireMoveReceiverEffectAdmission(view, principal, grant.PhaseCommandId, command, EvaluationClock.GetUtcNow());
    }
}
