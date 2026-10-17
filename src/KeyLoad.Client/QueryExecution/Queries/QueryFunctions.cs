namespace KeyLoad.Client;

/// <summary>Query markers for canonical metadata and the distinction between JSON null and an absent field.</summary>
public static class QueryFunctions
{
    /// <summary>Projects the canonical entity identifier in a typed query.</summary>
    /// <typeparam name="T">Record type used by the expression.</typeparam>
    /// <param name="document">Expression parameter marker.</param>
    /// <returns>The entity identifier when translated by the query builder.</returns>
    public static string DocumentId<T>(T document) => throw new InvalidOperationException(QueryPredicateTokens.MarkerExecutionMessage);
    /// <summary>Projects the canonical document revision in a typed query.</summary>
    /// <typeparam name="T">Record type used by the expression.</typeparam>
    /// <param name="document">Expression parameter marker.</param>
    /// <returns>The document revision when translated by the query builder.</returns>
    public static long DocumentRevision<T>(T document) => throw new InvalidOperationException(QueryPredicateTokens.MarkerExecutionMessage);
    /// <summary>Tests the canonical JSON null state in a typed query.</summary>
    /// <typeparam name="T">Value type used by the expression.</typeparam>
    /// <param name="value">Expression marker value.</param>
    /// <returns>A query result when translated by the query builder.</returns>
    public static bool IsNull<T>(T value) => throw new InvalidOperationException(QueryPredicateTokens.MarkerExecutionMessage);
    /// <summary>Tests the canonical JSON missing-field state in a typed query.</summary>
    /// <typeparam name="T">Value type used by the expression.</typeparam>
    /// <param name="value">Expression marker value.</param>
    /// <returns>A query result when translated by the query builder.</returns>
    public static bool IsMissing<T>(T value) => throw new InvalidOperationException(QueryPredicateTokens.MarkerExecutionMessage);
}
