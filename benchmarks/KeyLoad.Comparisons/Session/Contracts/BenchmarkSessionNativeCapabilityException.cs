namespace KeyLoad.Comparisons;

// Emitted only at an adapter's verified native capability boundary, never for operational failures.
internal sealed class BenchmarkSessionNativeCapabilityException : NotSupportedException
{
    public BenchmarkSessionNativeCapabilityException()
    {
    }
    public BenchmarkSessionNativeCapabilityException(string reason) : base(reason)
    {
    }
    public BenchmarkSessionNativeCapabilityException(string reason, Exception innerException) : base(reason, innerException)
    {
    }
}
