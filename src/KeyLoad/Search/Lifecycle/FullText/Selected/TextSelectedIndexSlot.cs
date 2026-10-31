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
    private readonly Lock gate = new();
    private IndexOfTokenRecordPreviousToken<ulong, ulong>? index;
    private HashSet<TextSelectedProjectionLease>? readers;
    private Exception? failure;
    private TextSelectedManifestCut? manifestCut;
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
            if ((readers?.Count ?? Empty) != Empty)
            {
                throw TextErrors.Ownership();
            }
        }
    }

    /// <summary>Returns the validated manifest, reading the file again only after maintenance rewrote it.</summary>
    internal TextSelectedManifestCut Manifest(Func<TextIncrementalManifest> read)
    {
        var file = new FileInfo(Path.Combine(root, leaf, TextIncrementalProtocol.ManifestFile));
        var (written, length) = file.Exists ? (file.LastWriteTimeUtc, file.Length) : (DateTime.MinValue, Empty);
        lock (gate)
        {
            if (manifestCut is { } cached && cached.Written == written && cached.Length == length)
            {
                return cached;
            }
        }
        var cut = TextSelectedManifestCut.Create(read(), written, length);
        lock (gate)
        {
            manifestCut = cut;
        }
        return cut;
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
