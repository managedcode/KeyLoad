using KeyLoad.Client;
using KeyLoad.Query;

namespace KeyLoad.Comparisons.Targets;

internal static class KeyLoadSessionQueryExecution
{
    private const string PostLoadIndexUnsupported = "NativePostLoadSecondaryIndexConstructionUnsupported";
    private const string IndexedReadCutMismatch = "SessionIndexedReadCutQueryMismatch";
    private const string EntityIdPath = "/@id";
    private const string NumberPath = "/number";
    private const string EqualOperator = "=";
    private const string AtLeastOperator = ">=";
    private const string BeforeOperator = "<";
    private const string AndOperator = "AND";
    private const string DocumentsCollection = "documents";
    private const string AllFields = "*";
    private const string PointAccessPath = "point";
    private const int ExpectedRows = 1;
    private const int FirstRow = 0;
    private const int MinimumCutPosition = 0;
    private const int InitialRevision = 1;
    private const int NumberUpperBoundOffset = 1;

    internal static Task BuildIndexesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        // Only existing native identical metadata or policy CAS is admitted after creation.
        return Task.FromException(new BenchmarkSessionNativeCapabilityException(PostLoadIndexUnsupported));
    }

    internal static async Task QueryAsync(KeyLoadClient client, PartitionRef partition, int ordinal,
        int stableStart, int stableCount, CancellationToken cancellationToken)
    {
        var expected = BenchmarkSessionQueryContract.Expected(ordinal, stableStart, stableCount);
        var filter = new Logical(new Comparison(new FieldOperand(EntityIdPath), EqualOperator, ValueOperand.Create(expected.Id)), AndOperator,
            new Logical(new Comparison(new FieldOperand(NumberPath), AtLeastOperator, ValueOperand.Create(expected.Number)), AndOperator,
                new Comparison(new FieldOperand(NumberPath), BeforeOperator, ValueOperand.Create(expected.Number + NumberUpperBoundOffset))));
        var page = KeyLoadClientResults.Success(await client.QueryAstAsync(new(partition,
            new(DocumentsCollection, null, [new(AllFields, AllFields)], filter,
                [new(EntityIdPath, false)], ExpectedRows)), cancellationToken).ConfigureAwait(false));
        if (page.AccessPath != PointAccessPath || page.CutPosition < MinimumCutPosition || page.Rows.Length != ExpectedRows || page.Cursor is not null
            || page.Rows[FirstRow].Redacted || page.Rows[FirstRow].Revision < InitialRevision)
        {
            throw new ComparisonFailureException(IndexedReadCutMismatch);
        }
        BenchmarkSessionQueryContract.Require(new(page.Rows[FirstRow].EntityId, page.Rows[FirstRow].Json), expected);
    }
}
