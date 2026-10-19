namespace KeyLoad.Server.Features.Search;

internal static class AnnSlotRetention
{
    private const int Empty = 0;
    internal static void RetireUnpinned(AnnRootLease root, List<AnnGenerationSlot> slots,
        AnnExecutionOptions options)
    {
        foreach (var slot in slots.Where(item => item.Retired && item.Readers == Empty).ToArray())
        {
            root.Remove(slot.Manifest.GenerationLeaf, options);
            slots.Remove(slot);
        }
        var receipt = root.Receipt;
        if (receipt.OwnedGenerations.Any(leaf => !slots.Any(slot => slot.Manifest.GenerationLeaf == leaf)
            && !receipt.Published.Any(item => item.Pointer.GenerationLeaf == leaf)
            && !receipt.Pending.Any(item => item.Pointer.GenerationLeaf == leaf)))
        {
            return;
        }
        foreach (var original in receipt.OriginalOperations.Where(entry => entry.Released))
        {
            if (!slots.Any(slot => SameKey(slot.Manifest, original.Key))
                && !receipt.Published.Any(item => item.Key == original.Key)
                && !receipt.Pending.Any(item => item.Key == original.Key))
            {
                root.CompleteOriginalRelease(original.Key, options);
            }
        }
    }

    internal static void RequireResident(List<AnnGenerationSlot> slots,
        AnnExecutionOptions options, long incoming)
    {
        var retained = incoming;
        foreach (var slot in slots)
        {
            retained = checked(retained + slot.Index.RetainedBytesUpperBound);
        }
        if (retained > options.MaximumResidentBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, AnnProtocol.Bound);
        }
    }

    internal static bool SameKey(AnnManifest manifest, AnnOwnedKey key)
        => manifest.Consumer == key.Consumer && manifest.IndexGeneration == key.IndexGeneration;
}
