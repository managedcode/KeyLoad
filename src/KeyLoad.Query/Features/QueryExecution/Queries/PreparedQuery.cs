using System.Collections.Immutable;
using System.Runtime.InteropServices;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Owns decoded field paths and the bounded, worst-first query prefix.</summary>
internal sealed class PreparedQuery
{
    private const int AdjacentElementOffset = 1;
    private const int FirstElementIndex = 0;
    private const int InitialSequence = 0;
    private const int EmptyElementCount = 0;
    private const int EqualOrder = 0;
    private const int NoRetainedBytes = 0;
    private const int VersionOne = 1;
    private const int HeapChildCount = 2;

    private readonly Dictionary<string, string[]> paths;
    private readonly ImmutableArray<Ordering> order;
    private readonly PriorityQueue<PreparedRow, PreparedRow>? ordinaryRows;
    private PreparedRow?[]? partitionRows;
    private readonly int capacity;
    private int rowCount;
    private readonly bool ordinalFullReferenceOrder;
    private readonly Func<DocumentRecord, QueryRow>? projectCandidate;
    private readonly Action<long>? reserveCandidate;
    private readonly Action<long>? releaseCandidate;

    internal PreparedQuery(SelectQuery query, int cursorOffset, int maxCandidates,
        bool ordinalFullReferenceOrder = false, Func<DocumentRecord, QueryRow>? projectCandidate = null,
        Action<long>? reserveCandidate = null, Action<long>? releaseCandidate = null)
    {
        order = query.Order;
        paths = PredicateEvaluator.Fields(query.Filter).Concat(order.Select(item => item.Path))
            .Concat(query.Projection.Where(item => item.Path != SqlSyntax.Star).Select(item => item.Path))
            .Distinct(StringComparer.Ordinal).ToDictionary(path => path, JsonData.PathSegments, StringComparer.Ordinal);
        capacity = (int)Math.Min(maxCandidates, (long)cursorOffset + query.Limit);
        this.ordinalFullReferenceOrder = ordinalFullReferenceOrder;
        this.projectCandidate = projectCandidate;
        this.reserveCandidate = reserveCandidate;
        this.releaseCandidate = releaseCandidate;
        if (ordinalFullReferenceOrder)
        {
            ArgumentNullException.ThrowIfNull(projectCandidate);
            ArgumentNullException.ThrowIfNull(reserveCandidate);
            ArgumentNullException.ThrowIfNull(releaseCandidate);
        }
        if (ordinalFullReferenceOrder)
        {
            partitionRows = new PreparedRow?[capacity + AdjacentElementOffset];
        }
        else
        {
            ordinaryRows = new(Comparer<PreparedRow>.Create((left, right) => -Math.Sign(Compare(left, right))));
        }
    }

    internal IReadOnlyDictionary<string, string[]> Paths => paths;
    internal int EligibleCount { get; private set; }

    internal void Consider(DocumentRecord document, JsonElement json)
    {
        EligibleCount++;
        var keys = new byte[order.Length][];
        for (var index = FirstElementIndex; index < order.Length; index++)
        {
            keys[index] = KeyCodec.Encode(PredicateEvaluator.FieldValue(order[index].Path, document, json, paths));
        }

        var reference = ordinalFullReferenceOrder ? null : KeyCodec.Encode(document.Reference.Id);
        var pending = new PreparedRow(document, keys, reference, null, InitialSequence);
        if (!ordinalFullReferenceOrder)
        {
            var queue = ordinaryRows!;
            if (queue.Count < capacity)
            {
                queue.Enqueue(pending, pending);
            }
            else if (capacity > EmptyElementCount && Compare(pending, queue.Peek()) < EqualOrder)
            {
                queue.Dequeue();
                queue.Enqueue(pending, pending);
            }
            return;
        }
        if (rowCount < capacity)
        {
            Insert(Retain(pending));
        }
        else if (capacity > EmptyElementCount && Compare(pending, partitionRows![FirstElementIndex]!) < EqualOrder)
        {
            var retained = Retain(pending);
            var removed = RemoveWorst();
            if (removed.ReservedBytes > NoRetainedBytes)
            {
                releaseCandidate?.Invoke(removed.ReservedBytes);
            }

            Insert(retained);
        }
    }

    internal PreparedRow[] Page(int offset, int limit, ReadExecutionBudget budget)
    {
        var available = ordinalFullReferenceOrder ? rowCount : ordinaryRows!.Count;
        var count = Math.Min(limit, Math.Max(EmptyElementCount, available - offset));
        var page = new PreparedRow[count];
        for (var index = count - AdjacentElementOffset; index >= EmptyElementCount; index--)
        {
            budget.Check();
            page[index] = ordinalFullReferenceOrder ? RemoveWorst() : ordinaryRows!.Dequeue();
            budget.Check();
        }
        return page;
    }

    internal ImmutableArray<PartitionQueryCandidateV1> DrainCandidates(int limit, ReadExecutionBudget budget,
        Action<long> reserveArray, Action releaseHeap)
    {
        if (!ordinalFullReferenceOrder)
        {
            throw new InvalidOperationException();
        }

        var count = Math.Min(limit, rowCount);
        reserveArray(PartitionQueryRetention.CandidateArrayBytes(count));
        var candidates = ImmutableArray.CreateBuilder<PartitionQueryCandidateV1>(count);
        candidates.Count = count;
        for (var index = count - AdjacentElementOffset; index >= EmptyElementCount; index--)
        {
            budget.Check();
            candidates[index] = RemoveWorst().Candidate ?? throw new InvalidOperationException();
        }
        partitionRows = null;
        releaseHeap();
        return candidates.MoveToImmutable();
    }

    private int Compare(PreparedRow left, PreparedRow right)
        => PartitionQueryOrder.Compare(left, right, order, ordinalFullReferenceOrder);

    private PreparedRow Retain(PreparedRow row)
    {
        var projected = projectCandidate!(row.Document!);
        var pendingKeys = row.PendingOrderKeys!;
        var retainedBytes = PartitionQueryRetention.CandidateBytes(row.Document!.Reference, projected, pendingKeys);
        reserveCandidate!(retainedBytes);
        var keys = ImmutableArray.CreateRange(pendingKeys.Select(static key =>
            ImmutableCollectionsMarshal.AsImmutableArray(key)));
        var candidate = new PartitionQueryCandidateV1(VersionOne, row.Document.Reference, projected, keys);
        var retained = row with
        {
            SourceDocument = null,
            PendingOrderKeys = null,
            Candidate = candidate,
            ReservedBytes = retainedBytes
        };
        return retained;
    }

    private void Insert(PreparedRow row)
    {
        var values = partitionRows!;
        var index = rowCount++;
        while (index > EmptyElementCount)
        {
            var parent = (index - AdjacentElementOffset) / HeapChildCount;
            if (Compare(row, values[parent]!) <= EqualOrder)
            {
                break;
            }

            values[index] = values[parent];
            index = parent;
        }
        values[index] = row;
    }

    private PreparedRow RemoveWorst()
    {
        var values = partitionRows!;
        var removed = values[FirstElementIndex]!;
        var replacement = values[--rowCount]!;
        values[rowCount] = null;
        if (rowCount == EmptyElementCount)
        {
            return removed;
        }

        var index = FirstElementIndex;
        while (true)
        {
            var child = index * HeapChildCount + AdjacentElementOffset;
            if (child >= rowCount)
            {
                break;
            }

            if (child + AdjacentElementOffset < rowCount && Compare(values[child + AdjacentElementOffset]!, values[child]!) > EqualOrder)
            {
                child++;
            }

            if (Compare(replacement, values[child]!) >= EqualOrder)
            {
                break;
            }

            values[index] = values[child];
            index = child;
        }
        values[index] = replacement;
        return removed;
    }
}

/// <summary>A candidate retained only while it belongs to the requested top prefix.</summary>
internal sealed record PreparedRow(DocumentRecord? SourceDocument, byte[][]? PendingOrderKeys, byte[]? IdKey,
    PartitionQueryCandidateV1? Candidate, long ReservedBytes)
{
    internal DocumentRecord Document => SourceDocument ?? throw new InvalidOperationException();
    internal EntityRef Reference => SourceDocument?.Reference ?? Candidate!.Reference;
}
