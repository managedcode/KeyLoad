using KeyLoad.Core.Features.ClusterRouting.Serialization;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string PhysicalCommandOwnerUnavailable = "The command physical owner is not admitted.";

    /// <summary>Checks configured local command ownership at the fresh quorum-applied native cut.</summary>
    /// <param name="owner">The server-confirmed local physical owner.</param>
    /// <param name="bootstrap">True only for native initial SCAT bootstrap.</param>
    /// <param name="cancellationToken">Original capability cancellation.</param>
    public void VerifyPhysicalCommandOwner(PhysicalShardRecord owner, bool bootstrap,
        CancellationToken cancellationToken) => Store.Read(view =>
            RequirePhysicalCommandOwner(view, owner, bootstrap, cancellationToken));

    internal void VerifyPhysicalCommandOwner(PhysicalShardRecord owner, bool bootstrap,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        Store.Read(raw => RequirePhysicalCommandOwner(work.CreateView(raw), owner, bootstrap, cancellationToken));
        work.Check();
    }

    private bool RequirePhysicalCommandOwner(IKeyValueView view, PhysicalShardRecord owner, bool bootstrap,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (Store.Identity.Incarnation != owner.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalCommandOwnerUnavailable);
        }
        var catalog = PhysicalShardCatalogRecordSerialization.Read(view);
        if (catalog is null)
        {
            if (!bootstrap)
            {
                throw Errors.Fail(ErrorCode.RecoveryRequired, PhysicalCommandOwnerUnavailable);
            }
            return true;
        }
        PhysicalShardCatalogValidation.ValidateCatalog(catalog);
        if (!PhysicalOwnerEntryValidation.SameOwner(catalog.DefaultShard, owner))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, PhysicalCommandOwnerUnavailable);
        }
        cancellationToken.ThrowIfCancellationRequested();
        return true;
    }
}
