using ManagedCode.Communication;
namespace KeyLoad.Clustering;

/// <summary>Publishes only copy counts observed after every required store returned its persisted result.</summary>
internal static class StorageCopyAcknowledgements
{
    internal static bool Same(Result<object> /* KLD0041: Preserve the current heterogeneous persisted batch-result shape while comparing copy decisions and attaching observed copy counts. */ first, Result<object> /* KLD0041: Preserve the current heterogeneous persisted batch-result shape while comparing copy decisions and attaching observed copy counts. */ second)
        => first.IsSuccess == second.IsSuccess &&
            (first.IsSuccess || Errors.Code(first.Problem).Value == Errors.Code(second.Problem).Value);

    internal static Result<object> /* KLD0041: Preserve the current heterogeneous persisted batch-result shape while comparing copy decisions and attaching observed copy counts. */ Attach(Result<object> /* KLD0041: Preserve the current heterogeneous persisted batch-result shape while comparing copy decisions and attaching observed copy counts. */ result, int copies)
    {
        if (result.IsFailed)
        {
            return result;
        }
        var /* KLD0041: Preserve the current heterogeneous persisted batch-result shape while comparing copy decisions and attaching observed copy counts. */ value = (result.Value /* KLD0041: Preserve the current heterogeneous persisted batch-result shape while comparing copy decisions and attaching observed copy counts. */) switch
        {
            CommitReceipt receipt /* KLD0042: The original persisted outcome identifies this exact receipt contract before its observed copy count is attached. */ => Copy(receipt, copies),
            CommitInboxResult inbox /* KLD0042: The original persisted outcome identifies this exact receipt contract before its observed copy count is attached. */ => inbox with { Receipt = Copy(inbox.Receipt, copies) },
            ProjectionBatchResult projection /* KLD0042: The original persisted outcome identifies this exact receipt contract before its observed copy count is attached. */ => projection with { Receipt = Copy(projection.Receipt, copies) },
            SubscriptionProcessingResult subscription /* KLD0042: The original persisted outcome identifies this exact receipt contract before its observed copy count is attached. */ => subscription with { Receipt = Copy(subscription.Receipt, copies) },
            DropTableResult drop /* KLD0042: The original persisted outcome identifies this exact receipt contract before its observed copy count is attached. */ => drop with { Receipt = Copy(drop.Receipt, copies) },
            _ => (result.Value /* KLD0041: Preserve the current heterogeneous persisted batch-result shape while comparing copy decisions and attaching observed copy counts. */)
        };
        return (Result<object>.Succeed(value!) /* KLD0041: Preserve the current heterogeneous persisted batch-result shape while comparing copy decisions and attaching observed copy counts. */);
    }

    private static CommitReceipt Copy(CommitReceipt receipt, int copies)
        => receipt with { PersistedCopies = receipt.Durability == DurabilityProfile.Buffered ? StorageCopyContract.UnpersistedCopies : copies };
}
