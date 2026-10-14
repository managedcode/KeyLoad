namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnSlotRetention
{
    private const int Empty = 0;
    internal static void RetireUnpinned(NativeAnnRootLease root, List<NativeAnnGenerationSlot> slots,
        NativeAnnExecutionOptions options)
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

    internal static void RequireResident(List<NativeAnnGenerationSlot> slots,
        NativeAnnExecutionOptions options, long incoming)
    {
        var retained = incoming;
        foreach (var slot in slots)
        {
            retained = checked(retained + slot.Index.RetainedBytesUpperBound);
        }
        if (retained > options.MaximumResidentBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAnnProtocol.Bound);
        }
    }

    internal static bool SameKey(NativeAnnManifest manifest, NativeAnnOwnedKey key)
        => manifest.Consumer == key.Consumer && manifest.IndexGeneration == key.IndexGeneration;
}
