using ZoneTree;
using ZoneTree.Transactional;
using PlainValue = ZoneTree.PresetTypes.Deletable<System.Memory<byte>>;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Merges the disjoint transactional and plain key spaces in their original byte order.</summary>
internal sealed class ZoneTreeMergedIterator(ITransactionIterator<Memory<byte>, Memory<byte>> transactional,
    IZoneTreeIterator<Memory<byte>, PlainValue> plain, bool reverse) : ITransactionIterator<Memory<byte>, Memory<byte>>
{
    private bool advanceTransactional = true;
    private bool advancePlain = true;
    private bool hasTransactional;
    private bool hasPlain;
    private bool selectedPlain;
    private const string DuplicateRecord = "A table record exists in both physical storage trees.";

    public Memory<byte> CurrentKey => selectedPlain ? plain.CurrentKey : transactional.CurrentKey;
    public Memory<byte> CurrentValue => selectedPlain ? plain.CurrentValue.Value : transactional.CurrentValue;
    public bool HasCurrent => hasTransactional || hasPlain;
    public KeyValuePair<Memory<byte>, Memory<byte>> Current => new(CurrentKey, CurrentValue);

    public bool Next()
    {
        if (advanceTransactional)
        {
            hasTransactional = transactional.Next();
        }
        if (advancePlain)
        {
            hasPlain = plain.Next();
        }
        if (!hasTransactional && !hasPlain)
        {
            return false;
        }
        var comparison = hasTransactional && hasPlain
            ? transactional.CurrentKey.Span.SequenceCompareTo(plain.CurrentKey.Span) : hasPlain ? 1 : -1;
        if (hasTransactional && hasPlain && comparison == 0)
        {
            throw Errors.Fail(ErrorCode.Corruption, DuplicateRecord);
        }
        selectedPlain = !hasTransactional || (hasPlain && (reverse ? comparison < 0 : comparison > 0));
        advancePlain = selectedPlain;
        advanceTransactional = !selectedPlain;
        return true;
    }

    public void Seek(in Memory<byte> key)
    {
        transactional.Seek(key);
        plain.Seek(key);
        Reset();
    }

    public void SeekFirst()
    {
        transactional.SeekFirst();
        plain.SeekFirst();
        Reset();
    }

    private void Reset()
    {
        advanceTransactional = true;
        advancePlain = true;
        hasTransactional = false;
        hasPlain = false;
    }

    public void Dispose()
    {
        var failures = new List<Exception>();
        ZoneTreeExistingStoreCleanup.Capture(transactional.Dispose, failures);
        ZoneTreeExistingStoreCleanup.Capture(plain.Dispose, failures);
        ZoneTreeExistingStoreCleanup.ThrowFailures(failures);
    }
}
