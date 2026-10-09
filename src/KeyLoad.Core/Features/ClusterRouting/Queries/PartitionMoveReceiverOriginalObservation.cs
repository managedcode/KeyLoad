using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Execution;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private long RequireMoveReceiverOriginalObservationEpoch(IKeyValueView view, PrincipalRecord principal,
        PartitionMovePeerEnvelope original, Guid originalId)
    {
        var issued = PartitionMoveReceiverIssuanceStorage.Read(view, original.Partition, original.MoveId,
            originalId, Limits.MaxBatchBytes)
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        if (principal.Id != issued.ReceiverPrincipalId)
        { throw Errors.Fail(ErrorCode.PermissionDenied, ClusterAdministrationRequiredMessage); }
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        PartitionMoveReceiverIssuanceValidation.Require(issued, originalId, original, issued.OriginalAuthorization,
            catalog.DefaultShard, Limits.MaxBatchBytes);
        var actual = CommandOutcomeKeyResolver.Select(view, principal.Id, issued.IssuanceCommandId,
            new CommandOutcomePartitionScope(CommandOutcomeScopeKind.Partition, original.Partition)).Outcome
            ?? throw Errors.Fail(ErrorCode.RecoveryRequired, PartitionMoveProtocol.MissingAuthority);
        var applied = view.ReadOwnedValue(KeySpace.AppliedBytes)
            ?? throw Errors.Fail(ErrorCode.Corruption, PartitionMoveProtocol.MissingAuthority);
        var cut = NativeSerialization.Deserialize<long>(applied);
        PartitionMoveReceiverIssuanceValidation.RequireStoredResult(issued, actual, Store.Identity.Incarnation, cut);
        return issued.ReceiverIssuancePolicyEpoch;
    }
}
