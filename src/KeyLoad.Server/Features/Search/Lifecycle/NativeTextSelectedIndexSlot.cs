using KeyLoad.Core;
using Microsoft.Extensions.Options;
using ZoneTree;
using ZoneTree.FullTextSearch;
using ZoneTree.FullTextSearch.Index;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextSelectedIndexSlot(string root, string leaf, Guid node,
    IOptions<NativeTextExecutionOptions> options) : IDisposable, INativeTextSelectedIndexLeaseOwner
{
    private const int Empty = 0;
    private readonly Lock gate = new();
    private IndexOfTokenRecordPreviousToken<ulong, ulong>? index;
    private int readers;
    private Exception? failure;
    private long successfulPostingReads;
    internal long SuccessfulPostingReads => Volatile.Read(ref successfulPostingReads);
    public void ObserveOriginalPostingRead() => Interlocked.Increment(ref successfulPostingReads);

    public void Enter(ReadExecutionBudget budget)
    {
        lock (gate)
        {
            if (failure is not null)
            { throw new AggregateException(failure); }
            budget.Check();
            index ??= NativeTextIndex.Open(Path.Combine(root, leaf, NativeTextProtocol.NativeDirectory),
                new NativeTextFileStreamProvider(root, leaf, node, options), options);
            index.IsReadOnly = true;
            readers++;
        }
    }

    public IZoneTreeIterator<CompositeKeyOfTokenRecordPrevious<ulong, ulong>, byte> CreateIterator()
    {
        lock (gate)
        {
            if (readers <= Empty || index is null)
            { throw NativeTextErrors.Ownership(); }
            return index.ZoneTree1.CreateIterator(IteratorType.NoRefresh, contributeToTheBlockCache: false);
        }
    }

    public void Exit()
    {
        lock (gate)
        {
            if (readers <= Empty)
            { throw NativeTextErrors.Corrupt(); }
            readers--;
            if (readers == Empty)
            {
                try
                { DisposeIndex(); }
                catch (Exception error) { failure = error; throw; }
            }
        }
    }

    internal void RequireSettled()
    {
        lock (gate)
        {
            if (failure is not null)
            { throw new AggregateException(failure); }
            if (readers != Empty || index is not null)
            { throw NativeTextErrors.Ownership(); }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (readers != Empty)
            { throw NativeTextErrors.Ownership(); }
            try
            { DisposeIndex(); }
            catch (Exception cleanup) when (failure is not null)
            { throw new AggregateException(failure, cleanup); }
            if (failure is not null)
            { throw new AggregateException(failure); }
        }
    }

    private void DisposeIndex()
    {
        if (index is not { } original)
        { return; }
        original.Dispose();
        index = null;
    }
}
