using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string BootstrapRequiresRevisionZero = "Physical shard catalog bootstrap requires expected revision zero.";
    private const string ExistingCatalogConflict = "The physical shard catalog conflicts with configured cluster identity.";
    internal void InitializeNativeResourceOwnerInventoryGenesis(IAtomicTransaction transaction)
    {
        if (configuredPhysicalOwner is { } owner
            && ResourceOwnerInventoryGenesis.CanInitialize(transaction, Store.Identity, Store.Position, owner, Limits))
        {
            ResourceOwnerInventoryCatalog.InitializeFresh(transaction, Store.Identity, owner, Limits);
        }
    }

    private OperationResult ExecuteBootstrapPhysicalShardCatalog(IAtomicTransaction transaction,
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
            var initial = PhysicalShardCatalogRecordSerialization.InitialCatalog(request);
            if (ResourceOwnerInventoryGenesis.CanInitialize(transaction, Store.Identity, Store.Position,
                initial.DefaultShard, Limits))
            {
                ResourceOwnerInventoryCatalog.InitializeFresh(transaction, Store.Identity, initial.DefaultShard, Limits);
            }
            transaction.PutRecord(key, initial);
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
