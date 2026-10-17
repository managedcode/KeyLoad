using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution.Execution;

namespace KeyLoad.Server;

/// <summary>Reads only the actual node-local committed catalog after its existing RF3 barrier.</summary>
internal static class PhysicalShardCatalogLocalRead
{
    internal const string CatalogMissing = "The physical shard catalog is not initialized.";

    internal static async Task<PhysicalShardCatalog?> ReadAsync(PartitionHost owner, CancellationToken cancellationToken)
    {
        await owner.Coordinator.ReadBarrierAsync(cancellationToken).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return owner.Database.Store.Read(view =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var catalog = PhysicalShardCatalogRecordSerialization.Read(view);
            if (catalog is not null)
            {
                PhysicalShardCatalogValidation.ValidateCatalog(catalog);
            }
            return catalog;
        });
    }

    internal static async Task<PhysicalShardCatalog?> ReadAsync(PartitionHost owner,
        ReadExecutionBudget work, ReadExecutionBudgetReadGrant? sourceMeta, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        await owner.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        work.Check();
        var result = owner.Database.Store.Read(raw =>
        {
            var view = sourceMeta is null ? work.CreateView(raw) : work.CreateView(raw, sourceMeta);
            var catalog = PhysicalShardCatalogRecordSerialization.Read(view);
            if (catalog is not null)
            {
                PhysicalShardCatalogValidation.ValidateCatalog(catalog);
            }
            return catalog;
        });
        work.Check();
        return result;
    }

}
