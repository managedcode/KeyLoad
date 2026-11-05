namespace KeyLoad.Clustering;

/// <summary>Projects the decisions actually persisted by a copy into the current replication response.</summary>
public static class StorageCopyBatchAcknowledgements
{
    /// <summary>Retains full outcomes only for the copy selected to answer the command's caller.</summary>
    public static StorageCopyBatchAcknowledgement Create(StorageBatchResult result, bool includeOutcome)
    {
        ArgumentNullException.ThrowIfNull(result) /* KLD0041: The BCL null guard checks this existing StorageBatchResult reference through its object parameter. */;
        return new(result.Position, [.. result.Applied.Select(Decision)], includeOutcome ? result : null);
    }

    internal static StorageCopyCommandAcknowledgement Decision(StorageOperationResult result)
        => new(result.Sequence,
            (result.Result /* KLD0041: Read only the persisted success decision from this existing mixed-operation result slot. */).IsSuccess,
            Errors.Code((result.Result /* KLD0041: Read only the persisted error code from this existing mixed-operation result slot. */).Problem).Value);
}
