using KeyLoad.Core;
using Microsoft.Extensions.Options;
using ZoneTree;
using ZoneTree.FullTextSearch;
using ZoneTree.FullTextSearch.Index;

namespace KeyLoad.Server.Search;

internal sealed class TextSelectedIndexSlot(string root, string leaf, Guid node,
    IOptions<TextExecutionOptions> options, TextResourceOwnership? resources = null) : IDisposable, ITextSelectedIndexLeaseOwner
{
    private const int Empty = 0;
    private const int SingleReader = 1;
    private readonly Lock gate = new();
    private IndexOfTokenRecordPreviousToken<ulong, ulong>? index;
    private HashSet<TextSelectedProjectionLease>? readers;
    private Exception? failure;
    private long successfulPostingReads;
    internal long SuccessfulPostingReads => Volatile.Read(ref successfulPostingReads);
    public void ObserveOriginalPostingRead() => Interlocked.Increment(ref successfulPostingReads);

    public void Enter(ReadExecutionBudget budget, TextSelectedProjectionLease reader)
    {
        lock (gate)
        {
            if (failure is not null)
            {
                throw new AggregateException(failure);
            }
            budget.Check();
            if (readers is null)
            {
                readers = new(ReferenceEqualityComparer.Instance);
            }
            if (!readers.Add(reader))
            {
                throw TextErrors.Ownership();
            }
            try
            {
                index ??= RuntimeTextIndex.Open(Path.Combine(root, leaf, TextProtocol.RuntimeDirectoryCore),
                    new TextFileStreamProvider(root, leaf, node, options, resources, allowReadOnlyInventory: true), options);
                index.IsReadOnly = true;
            }
            catch (Exception error) { failure = error; throw; }
        }
    }

    public IZoneTreeIterator<CompositeKeyOfTokenRecordPrevious<ulong, ulong>, byte> CreateIterator()
    {
        lock (gate)
        {
            if ((readers?.Count ?? Empty) <= Empty || index is null)
            {
                throw TextErrors.Ownership();
            }
            return index.ZoneTree1.CreateIterator(IteratorType.NoRefresh, contributeToTheBlockCache: false);
        }
    }

    public void Exit(TextSelectedProjectionLease reader)
    {
        lock (gate)
        {
            if (readers is null || !readers.Contains(reader))
            {
                throw TextErrors.Corrupt();
            }
            if (readers.Count == SingleReader)
            {
                try
                {
                    DisposeIndex();
                }
                catch (Exception error) { failure = error; throw; }
            }
            _ = readers.Remove(reader);
        }
    }

    internal void RequireReaderSettled(TextSelectedProjectionLease reader)
    {
        lock (gate)
        {
            if (readers?.Contains(reader) == true)
            {
                throw TextErrors.Ownership();
            }
        }
    }

    internal void RequireSettled()
    {
        lock (gate)
        {
            if (failure is not null)
            {
                throw new AggregateException(failure);
            }
            if ((readers?.Count ?? Empty) != Empty || index is not null)
            {
                throw TextErrors.Ownership();
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if ((readers?.Count ?? Empty) != Empty)
            {
                throw TextErrors.Ownership();
            }
            try
            {
                DisposeIndex();
            }
            catch (Exception cleanup) when (failure is not null)
            {
                throw new AggregateException(failure, cleanup);
            }
            if (failure is not null)
            {
                throw new AggregateException(failure);
            }
        }
    }

    private void DisposeIndex()
    {
        if (index is not { } original)
        {
            return;
        }
        original.Dispose();
        index = null;
    }
}
