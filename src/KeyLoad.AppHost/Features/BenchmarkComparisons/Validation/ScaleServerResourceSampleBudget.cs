namespace KeyLoad.AppHost.Features.BenchmarkComparisons;

internal sealed class ScaleServerResourceSampleBudget(int maximum)
{
    private int _used;
    internal int Remaining => maximum - _used;

    internal void Charge(int bytes)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(bytes);
        if (bytes > Remaining) throw new InvalidDataException("Server resource sample exceeded its bound.");
        _used += bytes;
    }
}
