namespace KeyLoad.Comparisons;

/// <summary>Actual native index creation receipt.</summary>
/// <param name="IndexKind">The method created by the database.</param>
/// <param name="Definition">The native index definition.</param>
/// <param name="Parameters">The actual index and query parameters.</param>
/// <param name="BuildMilliseconds">The observed index build duration.</param>
public sealed record VectorIndexReceipt(VectorIndexKind IndexKind, string Definition,
    IReadOnlyDictionary<string, string> Parameters, double BuildMilliseconds);
