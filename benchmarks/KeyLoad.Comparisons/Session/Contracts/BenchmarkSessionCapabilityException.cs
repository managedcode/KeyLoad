namespace KeyLoad.Comparisons;

// Emitted only at an adapter's verified execution capability boundary, never for operational failures.
internal sealed class BenchmarkSessionCapabilityException : NotSupportedException
{
    public BenchmarkSessionCapabilityException()
    {
    }
    public BenchmarkSessionCapabilityException(string reason) : base(reason)
    {
    }
    public BenchmarkSessionCapabilityException(string reason, Exception innerException) : base(reason, innerException)
    {
    }
}
