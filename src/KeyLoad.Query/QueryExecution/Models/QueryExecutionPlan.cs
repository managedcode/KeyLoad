using System.Collections.Immutable;

namespace KeyLoad.Query.QueryExecution;

/// <summary>Describes physical work without executing candidates or inventing cost estimates.</summary>
internal sealed record QueryExecutionPlan(string AccessPath, string AtomicPartition, long ReadCut,
    string Scan, string? Index, ImmutableArray<string> SeekFields, bool HasLowerBound, bool HasUpperBound,
    ImmutableArray<string> ResidualFields, ImmutableArray<Ordering> Order, long RetainedRows, int Limit, int Offset,
    ImmutableArray<Selection> Projection, ImmutableArray<string> Operators);
