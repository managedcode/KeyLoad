using KeyLoad.AppHost.Features.BenchmarkComparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ScaleServerMemoryCurrentFormatTests
{
    private const string NativeCounter = "123456\n";
    private const string OuterWhitespaceCounter = " \t123456\r\n ";
    private const string NativeZero = "0\n";
    private const string NativeMaximum = "9223372036854775807\n";
    private const string SignedPositive = "+123456\n";
    private const string Negative = "-1\n";
    private const string Overflow = "9223372036854775808\n";
    private const string Fractional = "123456.0\n";
    private const string InteriorSpace = "123 456\n";
    private const string ExtraLine = "123456\n0\n";
    private const string Unlimited = "max\n";
    private const string Empty = "";
    private const long CounterBytes = 123456;
    private const long ZeroBytes = 0;

    [Test]
    [Arguments(NativeCounter, CounterBytes)]
    [Arguments(OuterWhitespaceCounter, CounterBytes)]
    [Arguments(NativeZero, ZeroBytes)]
    [Arguments(NativeMaximum, long.MaxValue)]
    public async Task AcScale016NativeMemoryCounterFileWhitespacePreservesExactBytes(string text, long expected)
    {
        await Assert.That(ScaleServerProcessMetrics.TryMemoryCurrent(text, out var bytes)).IsTrue();
        await Assert.That(bytes).IsEqualTo(expected);
    }

    [Test]
    [Arguments(SignedPositive)]
    [Arguments(Negative)]
    [Arguments(Overflow)]
    [Arguments(Fractional)]
    [Arguments(InteriorSpace)]
    [Arguments(ExtraLine)]
    [Arguments(Unlimited)]
    [Arguments(Empty)]
    public async Task AcScale016MalformedMemoryCounterRejectsThenHealthyFollowUp(string text)
    {
        await Assert.That(ScaleServerProcessMetrics.TryMemoryCurrent(text, out _)).IsFalse();
        await Assert.That(ScaleServerProcessMetrics.TryMemoryCurrent(NativeCounter, out var bytes)).IsTrue();
        await Assert.That(bytes).IsEqualTo(CounterBytes);
    }
}
