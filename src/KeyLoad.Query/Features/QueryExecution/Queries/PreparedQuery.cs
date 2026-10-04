using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.Query.Features.QueryExecution;

/// <summary>Owns decoded field paths and the bounded, worst-first query prefix.</summary>
internal sealed class PreparedQuery
{
    private readonly Dictionary<string, string[]> paths;
    private readonly ImmutableArray<Ordering> order;
    private readonly PriorityQueue<PreparedRow, PreparedRow> rows;
    private readonly int capacity;

    internal PreparedQuery(SelectQuery query, int cursorOffset, int maxCandidates)
    {
        order = query.Order;
        paths = PredicateEvaluator.Fields(query.Filter).Concat(order.Select(item => item.Path))
            .Concat(query.Projection.Where(item => item.Path != "*").Select(item => item.Path))
            .Distinct(StringComparer.Ordinal).ToDictionary(path => path, JsonData.PathSegments, StringComparer.Ordinal);
        capacity = (int)Math.Min(maxCandidates, (long)cursorOffset + query.Limit);
        rows = new(Comparer<PreparedRow>.Create((left, right) => -Math.Sign(Compare(left, right))));
    }

    internal IReadOnlyDictionary<string, string[]> Paths => paths;
    internal int EligibleCount { get; private set; }

    internal void Consider(DocumentRecord document, JsonElement json)
    {
        EligibleCount++;
        var keys = new byte[order.Length][];
        for (var index = 0; index < order.Length; index++)
        {
            keys[index] = KeyCodec.Encode(PredicateEvaluator.FieldValue(order[index].Path, document, json, paths));
        }
        var row = new PreparedRow(document, keys, KeyCodec.Encode(document.Reference.Id));
        if (rows.Count < capacity)
        {
            rows.Enqueue(row, row);
        }
        else if (Compare(row, rows.Peek()) < 0)
        {
            rows.Dequeue();
            rows.Enqueue(row, row);
        }
    }

    internal PreparedRow[] Page(int offset, int limit, ReadExecutionBudget budget)
    {
        var count = Math.Min(limit, Math.Max(0, rows.Count - offset));
        var page = new PreparedRow[count];
        for (var index = count - 1; index >= 0; index--)
        {
            budget.Check();
            page[index] = rows.Dequeue();
            budget.Check();
        }
        return page;
    }

    private int Compare(PreparedRow left, PreparedRow right)
    {
        for (var index = 0; index < order.Length; index++)
        {
            var result = left.OrderKeys[index].AsSpan().SequenceCompareTo(right.OrderKeys[index]);
            if (result != 0)
            {
                return order[index].Descending ? -Math.Sign(result) : Math.Sign(result);
            }
        }
        return left.IdKey.AsSpan().SequenceCompareTo(right.IdKey);
    }
}

/// <summary>A candidate retained only while it belongs to the requested top prefix.</summary>
internal sealed record PreparedRow(DocumentRecord Document, byte[][] OrderKeys, byte[] IdKey);
