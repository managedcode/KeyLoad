namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class NonSeekableResponseStream(byte[] body) : MemoryStream(body, writable: false)
{
    public override bool CanSeek => false;
}
