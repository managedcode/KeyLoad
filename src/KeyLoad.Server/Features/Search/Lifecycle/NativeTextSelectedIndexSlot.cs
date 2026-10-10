using System.Runtime.CompilerServices;
using KeyLoad.Core;
using Microsoft.Extensions.Options;
using ZoneTree;
using ZoneTree.FullTextSearch;
using ZoneTree.FullTextSearch.Index;

namespace KeyLoad.Server.Features.Search;

internal sealed class NativeTextSelectedIndexSlot(string root, string leaf, Guid node,
    IOptions<NativeTextExecutionOptions> options, NativeTextResourceOwnership? resources = null) : IDisposable, INativeTextSelectedIndexLeaseOwner
{
    private const int Empty = 0;
    private const int SingleReader = 1;
    private const int ReferenceLedgerNativeCapacity = 3;
    private readonly Lock gate = new();
    private IndexOfTokenRecordPreviousToken<ulong, ulong>? index;
    private HashSet<NativeTextSelectedProjectionLease>? readers;
    private Exception? failure;
    private long successfulPostingReads;
    internal long SuccessfulPostingReads => Volatile.Read(ref successfulPostingReads);
    public void ObserveOriginalPostingRead() => Interlocked.Increment(ref successfulPostingReads);

    public void Enter(ReadExecutionBudget budget, NativeTextSelectedProjectionLease reader)
    {
        lock (gate)
        {
            if (failure is not null)
            { throw new AggregateException(failure); }
            budget.Check();
            if (readers is null)
            {
                budget.ChargeBytes(checked(NativeTextIncrementalSourceProtocol.MapSlotBytes
                    + (long)ReferenceLedgerNativeCapacity * (sizeof(int)
                    + Unsafe.SizeOf<(int Hash, int Next, NativeTextSelectedProjectionLease Reader)>())));
                readers = new(options.Value.MaximumActiveLeases, ReferenceEqualityComparer.Instance);
            }
            if (!readers.Add(reader))
            { throw NativeTextErrors.Ownership(); }
            try
            {
                index ??= NativeTextIndex.Open(Path.Combine(root, leaf, NativeTextProtocol.NativeDirectory),
                    new NativeTextFileStreamProvider(root, leaf, node, options, resources, allowReadOnlyInventory: true), options);
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
            { throw NativeTextErrors.Ownership(); }
            return index.ZoneTree1.CreateIterator(IteratorType.NoRefresh, contributeToTheBlockCache: false);
        }
    }

    public void Exit(NativeTextSelectedProjectionLease reader)
    {
        lock (gate)
        {
            if (readers is null || !readers.Contains(reader))
            { throw NativeTextErrors.Corrupt(); }
            if (readers.Count == SingleReader)
            {
                try
                { DisposeIndex(); }
                catch (Exception error) { failure = error; throw; }
            }
            _ = readers.Remove(reader);
        }
    }

    internal void RequireReaderSettled(NativeTextSelectedProjectionLease reader)
    {
        lock (gate)
        {
            if (readers?.Contains(reader) == true)
            { throw NativeTextErrors.Ownership(); }
        }
    }

    internal void RequireSettled()
    {
        lock (gate)
        {
            if (failure is not null)
            { throw new AggregateException(failure); }
            if ((readers?.Count ?? Empty) != Empty || index is not null)
            { throw NativeTextErrors.Ownership(); }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if ((readers?.Count ?? Empty) != Empty)
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
