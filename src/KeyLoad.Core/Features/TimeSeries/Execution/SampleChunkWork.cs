namespace KeyLoad.Core.Features.TimeSeries;

// Ordered apply uses the already admitted deterministic codec/transaction bounds.
// It must not make replica-local cancellation or elapsed-clock decisions.
internal readonly struct SampleChunkWork
{
    private readonly ReadExecutionBudget? original;

    private SampleChunkWork(ReadExecutionBudget original) => this.original = original;

    internal static SampleChunkWork Ordered => default;

    internal static SampleChunkWork Observe(ReadExecutionBudget original)
    {
        ArgumentNullException.ThrowIfNull(original);
        return new(original);
    }

    public static implicit operator SampleChunkWork(ReadExecutionBudget original) => Observe(original);

    internal void Check() => original?.Check();
}
