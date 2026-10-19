using ManagedCode.Communication;
namespace KeyLoad.Clustering;

/// <summary>Publishes only copy counts observed after every required store returned its persisted result.</summary>
internal static class StorageCopyAcknowledgements
{
    internal static bool Same(Result<object> first, Result<object> second)
        => first.IsSuccess == second.IsSuccess &&
            (first.IsSuccess || Errors.Code(first.Problem) == Errors.Code(second.Problem));

    internal static Result<object> Attach(Result<object> result, int copies)
    {
        if (result.IsFailed)
        {
            return result;
        }
        var value = result.Value switch
        {
            CommitReceipt receipt => Copy(receipt, copies),
            CommitInboxResult inbox => inbox with { Receipt = Copy(inbox.Receipt, copies) },
            ProjectionBatchResult projection => projection with { Receipt = Copy(projection.Receipt, copies) },
            SubscriptionProcessingResult subscription => subscription with { Receipt = Copy(subscription.Receipt, copies) },
            DropTableResult drop => drop with { Receipt = Copy(drop.Receipt, copies) },
            _ => result.Value
        };
        return Result<object>.Succeed(value!);
    }

    private static CommitReceipt Copy(CommitReceipt receipt, int copies)
        => receipt with { PersistedCopies = receipt.Durability == DurabilityProfile.Buffered ? StorageCopyContract.UnpersistedCopies : copies };
}
