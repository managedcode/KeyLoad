namespace KeyLoad.Core;
/// <summary>Executes StorageExecutionValidationStorageOwnerValidation operations against the caller's database context.</summary>
public static class StorageExecutionValidationStorageOwnerValidation
{
    internal const string StorageOwnerMismatch = "The operation targets a different physical storage owner.";
    /// <summary>Verifies the actual local files, against the local store identity.</summary>

    /// <param name = "engine">The shared database execution context.</param>
    /// <param name = "owner">The owner.</param>
    /// <param name = "cancellationToken">The cancellation Token.</param>
    public static void VerifyStorageOwner(this DatabaseEngine engine, PhysicalShardRecord owner, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(engine);
        ArgumentNullException.ThrowIfNull(owner);
        cancellationToken.ThrowIfCancellationRequested();
        var identity = engine.Store.Identity;
        if (owner.PhysicalShardId != identity.NodeId || owner.Incarnation != identity.Incarnation)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, StorageOwnerMismatch);
        }
    }

    internal static void VerifyStorageOwner(this DatabaseEngine engine, PhysicalShardRecord owner, ReadExecutionBudget work, CancellationToken token)
    {
        work.Check();
        engine.VerifyStorageOwner(owner, token);
        work.Check();
    }
}
