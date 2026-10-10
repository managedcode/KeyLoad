using KeyLoad.Core.Features.Messaging;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private static void RequireRemoteTransferFailureWitness(RemoteTransferAcceptFailureClaims witness,
        RemoteTransferCoordinationHint hint)
    {
        if (witness is null || witness.OriginalAuthority?.Dependency is null || witness.CurrentDependency is null
            || witness.TargetCut is null || witness.Purpose != RemoteTransferAttemptProtocol.FailurePurpose
            || !Enum.IsDefined(witness.FailureKind) || !RemoteTransferDependencyShape.Digest(witness.OutcomeDigest)
            || !RemoteTransferDependencyShape.Valid(witness.OriginalAuthority.Dependency)
            || !RemoteTransferDependencyShape.Valid(witness.CurrentDependency)
            || witness.ReadGeneration < RemoteTransferAttemptProtocol.NoUsage
            || witness.TargetCut.Position < RemoteTransferAttemptProtocol.MinimumNativePosition
            || !RemoteTransferAttemptOriginalIdentity.Matches(hint, witness.OriginalAuthority)
            || witness.TargetCut.Incarnation != witness.OriginalAuthority.TargetIncarnation
            || witness.TargetCut.AtomicPartitionId != hint.Destination.Partition.AtomicPartitionId
            || witness.OriginalAuthority.AcceptCommandId != RemoteTransferAttemptIdentity.AcceptId(hint, hint.AcceptGeneration))
        { throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteTransferAttemptProtocol.Invalid); }
        JsonData.Identifier(witness.OutcomeDigest);
    }
}
