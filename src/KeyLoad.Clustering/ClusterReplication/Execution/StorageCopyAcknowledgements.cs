namespace KeyLoad.Clustering;

/// <summary>Publishes only copy counts observed after every required store returned its persisted result.</summary>
internal static class StorageCopyAcknowledgements
{
    internal static bool Same(OperationResult first, OperationResult second)
        => NativeSerialization.Serialize(first).AsSpan().SequenceEqual(NativeSerialization.Serialize(second));

    internal static OperationResult Attach(OperationResult result, int copies)
    {
        if (result.Error is not null)
        {
            return result;
        }
        var value = result.NativeValue switch
        {
            CommitReceipt receipt => Copy(receipt, copies),
            CommitInboxResult inbox => inbox with { Receipt = Copy(inbox.Receipt, copies) },
            ProjectionBatchResult projection => projection with { Receipt = Copy(projection.Receipt, copies) },
            SubscriptionProcessingResult subscription => subscription with { Receipt = Copy(subscription.Receipt, copies) },
            DropTableResult drop => drop with { Receipt = Copy(drop.Receipt, copies) },
            BlobCommitResult<BlobUploadInfo> upload => upload with { Receipt = Copy(upload.Receipt, copies) },
            BlobCommitResult<BlobMetadata> blob => blob with { Receipt = Copy(blob.Receipt, copies) },
            BlobCommitResult<BlobReclaimResult> reclaim => reclaim with { Receipt = Copy(reclaim.Receipt, copies) },
            _ => result.NativeValue
        };
        return result with { NativeValue = value };
    }

    private static CommitReceipt Copy(CommitReceipt receipt, int copies)
        => receipt with { PersistedCopies = receipt.Durability == DurabilityProfile.Buffered ? StorageCopyContract.UnpersistedCopies : copies };
}
