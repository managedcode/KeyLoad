namespace KeyLoad.Comparisons;

public sealed partial class ComparisonExecutionOptions
{
    internal const int DefaultClickHouseInsertFlushMilliseconds = 10;

    /// <summary>The maximum native async-insert buffering interval; acknowledgements still wait for the actual flush.</summary>
    public int ClickHouseInsertFlushMilliseconds { get; set; } = DefaultClickHouseInsertFlushMilliseconds;
}
