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
    }

    internal static void RequireResident(List<NativeAnnGenerationSlot> slots,
        NativeAnnExecutionOptions options, long incoming)
    {
        var retained = incoming;
        foreach (var slot in slots)
        { retained = checked(retained + slot.Index.RetainedBytesUpperBound); }
        if (retained > options.MaximumResidentBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, NativeAnnProtocol.Bound); }
    }

    internal static bool SameKey(NativeAnnManifest manifest, NativeAnnOwnedKey key)
        => manifest.Consumer == key.Consumer && manifest.IndexGeneration == key.IndexGeneration;
}
