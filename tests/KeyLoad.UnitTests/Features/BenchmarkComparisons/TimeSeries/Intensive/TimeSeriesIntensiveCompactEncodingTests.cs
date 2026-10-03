using System.Runtime.CompilerServices;
using KeyLoad.Comparisons.Features.BenchmarkComparisons.TimeSeries.Intensive;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons.TimeSeries.Intensive;

internal sealed class TimeSeriesIntensiveCompactEncodingTests
{
    [Test]
    public async Task AcTsi004HashRetainsThirtyTwoBytesAndExactOrderWithoutReferences()
    {
        const string value = "000102030405060708090a0b0c0d0e0f101112131415161718191a1b1c1d1e1f";
        var hash = TimeSeriesIntensiveHash.Parse(value);
        await Assert.That(hash.ToHex()).IsEqualTo(value);
        await Assert.That(hash).IsEqualTo(new TimeSeriesIntensiveHash(0x0001020304050607, 0x08090a0b0c0d0e0f,
            0x1011121314151617, 0x18191a1b1c1d1e1f));
        await Assert.That(Unsafe.SizeOf<TimeSeriesIntensiveHash>()).IsEqualTo(32);
        await Assert.That(RuntimeHelpers.IsReferenceOrContainsReferences<TimeSeriesIntensiveAttempt>()).IsFalse();
        Assert.ThrowsExactly<FormatException>(() => TimeSeriesIntensiveHash.Parse(value[..62]));
        Assert.ThrowsExactly<FormatException>(() => TimeSeriesIntensiveHash.Parse("z" + value[1..]));
    }

    [Test]
    public async Task AcTsi004SqlStatePreservesFiveAsciiCharactersAndRejectsInvalidInput()
    {
        await Assert.That(TimeSeriesIntensiveFailure.PackSqlState("23505")).IsEqualTo(0x3233353035UL);
        await Assert.That(TimeSeriesIntensiveFailure.PackSqlState("XX000")).IsEqualTo(0x5858303030UL);
        foreach (var invalid in new[] { "", "2350", "235050", "xx000", "23é05", "23\0\0\0" })
        {
            Assert.ThrowsExactly<FormatException>(() => TimeSeriesIntensiveFailure.PackSqlState(invalid));
        }
    }
}
