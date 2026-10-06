using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string BootstrapRequiresRevisionZero = "Physical shard catalog bootstrap requires expected revision zero.";
    private const string ExistingCatalogConflict = "The physical shard catalog conflicts with configured cluster identity.";
    private static OperationResult ExecuteBootstrapPhysicalShardCatalog(IAtomicTransaction transaction,
        ReplicatedOperation operation)
    {
        const int EmptyExpectedRevision = 0;

        var request = Payload<BootstrapPhysicalShardCatalogRequest>(operation);
        PhysicalShardCatalogValidation.ValidateRequest(request);
        if (request.ExpectedRevision != EmptyExpectedRevision)
        {
            throw Errors.Fail(ErrorCode.Conflict, BootstrapRequiresRevisionZero);
        }

        var key = PhysicalShardCatalogRecordSerialization.CatalogKey();
        var current = PhysicalShardCatalogRecordSerialization.Read(transaction);
        if (current is null)
        {
            transaction.PutRecord(key, PhysicalShardCatalogRecordSerialization.InitialCatalog(request));
            return Result(true);
        }

        PhysicalShardCatalogValidation.ValidateCatalog(current);
        if (current.Revision == PhysicalShardCatalogProtocol.InitialRevision
            && PhysicalShardCatalogValidation.MatchesInitial(current.DefaultShard, request))
        {
            return Result(true);
        }

        throw Errors.Fail(ErrorCode.Conflict, ExistingCatalogConflict);
    }
}
