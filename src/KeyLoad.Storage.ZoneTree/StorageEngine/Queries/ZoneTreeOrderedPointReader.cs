using ZoneTree.Transactional;

namespace KeyLoad.Storage.ZoneTree;

/// <summary>Uses one snapshot cursor for increasing point keys and seeks over gaps.</summary>
internal sealed class ZoneTreeOrderedPointReader : IOrderedPointReader
{
    private const string InvalidScope = "Ordered point reads require a transactional read scope.";
    private readonly ZoneTreeReadCounters counters;
    private readonly ZoneTreeOperationContext operation;
    private readonly byte[] prefix;
    private readonly CancellationToken cancellationToken;
    private ITransactionIterator<Memory<byte>, Memory<byte>>? iterator;
    private bool exhausted;
    private bool disposed;

    internal ZoneTreeOrderedPointReader(ZoneTreeStoreRuntime runtime, byte[] prefix, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        operation = runtime.CurrentOperation;
        if (operation.Writable || ZoneTreeDataRouting.IsPlain(runtime, prefix))
        {
            throw new InvalidOperationException(InvalidScope);
        }
        counters = runtime.ReadCounters;
        this.prefix = prefix;
        this.cancellationToken = cancellationToken;
    }

    public bool Read(ReadOnlySpan<byte> suffix, StorageValueReader reader)
    {
        ObjectDisposedException.ThrowIf(disposed, typeof(ZoneTreeOrderedPointReader));
        operation.Check();
        cancellationToken.ThrowIfCancellationRequested();
        var found = !exhausted && Locate(suffix);
        var value = found ? iterator!.CurrentValue : default;
        // Point diagnostics count the logical lookup's key and matched value, not cursor lookahead or physical I/O.
        counters.Point(owned: false, (long)prefix.Length + suffix.Length + value.Length);
        cancellationToken.ThrowIfCancellationRequested();
        if (found)
        {
            reader(value.Span);
        }
        return found;
    }

    private bool Locate(ReadOnlySpan<byte> suffix)
    {
        if (iterator is null)
        {
            iterator = operation.Snapshot.Transaction.CreateIterator(ZoneTreeSnapshotCursor.CreateTransactionalOptions());
            Seek(suffix);
        }
        else if (CompareCurrent(suffix) < 0)
        {
            // Adjacent keys cost one advance. At most one unmatched row is visited before seeking a gap.
            Advance();
            if (!exhausted && CompareCurrent(suffix) < 0)
            {
                Seek(suffix);
            }
        }
        return !exhausted && CompareCurrent(suffix) == 0;
    }

    private int CompareCurrent(ReadOnlySpan<byte> suffix) => iterator!.CurrentKey.Span[prefix.Length..].SequenceCompareTo(suffix);

    private void Seek(ReadOnlySpan<byte> suffix)
    {
        cancellationToken.ThrowIfCancellationRequested();
        iterator!.Seek(KeyCodec.JoinEncoded(prefix, suffix));
        Advance();
    }

    private void Advance()
    {
        cancellationToken.ThrowIfCancellationRequested();
        exhausted = !iterator!.Next() || !iterator.CurrentKey.Span.StartsWith(prefix);
    }

    public void Dispose()
    {
        if (!disposed)
        {
            iterator?.Dispose();
            disposed = true;
        }
    }
}
