namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string StorageOwnerMismatch = "The operation targets a different physical storage owner.";

    /// <summary>Verifies the actual local files, against the local store identity.</summary>
    public void VerifyStorageOwner(PhysicalShardRecord owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(owner);
        cancellationToken.ThrowIfCancellationRequested();
        var identity = Store.Identity;
        if (owner.PhysicalShardId != identity.NodeId || owner.Incarnation != identity.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, StorageOwnerMismatch);
        }
    }

    internal void VerifyStorageOwner(PhysicalShardRecord owner, ReadExecutionBudget work, CancellationToken token)
    {
        work.Check();
        VerifyStorageOwner(owner, token);
        work.Check();
    }
}
