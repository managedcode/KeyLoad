using KeyLoad.Storage;

namespace KeyLoad.UnitTests;

public sealed class KeyCodecTests
{
    [Fact]
    public void GoldenKeysRemainStable()
    {
        Assert.Equal("0111", Convert.ToHexString(KeyCodec.Encode((object?)null)));
        Assert.Equal("01308000000000000000", Convert.ToHexString(KeyCodec.Encode(0L)));
        Assert.Equal("0150610000", Convert.ToHexString(KeyCodec.Encode("a")));
        Assert.NotEqual(KeyCodec.Encode((object?)null), KeyCodec.Encode(MissingValue.Instance));
    }
    [Fact]
    public void TenThousandSeededDecimalKeysRoundTripAndSortNumerically()
    {
        var random = new Random(1701);
        var numbers = Enumerable.Range(0, 10_000).Select(_ => new decimal(random.Next(), random.Next(), random.Next(),
            random.Next(2) == 0, (byte)random.Next(29))).Concat([decimal.MinValue, decimal.MaxValue, 0, -1, 1, 0.0000000000000000000000000001m]).ToArray();
        foreach (var number in numbers) Assert.Equal(number, Assert.IsType<decimal>(Assert.Single(KeyCodec.Decode(KeyCodec.Encode(number)))));
        var encodedOrder = numbers.OrderBy(n => KeyCodec.Encode(n), BinaryKeyComparer.Instance).ToArray();
        Assert.Equal(numbers.Order().ToArray(), encodedOrder);
    }
    [Fact]
    public void StringsUseUtf8OrdinalOrderingAndEscapedComponentsDoNotCollide()
    {
        var samples = new[] { "", "a", "a\0", "a\0b", "aa", "b", "é", "ї", "😀", "\uffff" };
        foreach (var value in samples) Assert.Equal(value, KeyCodec.Decode(KeyCodec.Encode(value))[0]);
        var expected = samples.OrderBy(s => System.Text.Encoding.UTF8.GetBytes(s), BinaryKeyComparer.Instance).ToArray();
        Assert.Equal(expected, samples.OrderBy(s => KeyCodec.Encode(s), BinaryKeyComparer.Instance).ToArray());
        Assert.NotEqual(KeyCodec.Encode("ab", "c"), KeyCodec.Encode("a", "bc"));
        Assert.Equal(0, BinaryKeyComparer.Instance.Compare(KeyCodec.Encode(1.00m), KeyCodec.Encode(1m)));
    }
    [Fact]
    public void Int64AndFiniteDoublesPreserveOrder()
    {
        long[] integers = [long.MinValue, -1, 0, 1, long.MaxValue];
        double[] values = [double.MinValue, -1e100, -1, -double.Epsilon, 0, double.Epsilon, 1, double.MaxValue];
        Assert.Equal(integers, integers.OrderBy(v => KeyCodec.Encode(v), BinaryKeyComparer.Instance));
        Assert.Equal(values, values.OrderBy(v => KeyCodec.Encode(v), BinaryKeyComparer.Instance));
        Assert.Throws<KeyLoadException>(() => KeyCodec.Encode(double.NaN));
        Assert.Throws<KeyLoadException>(() => KeyCodec.Decode([1, 0xff]));
    }
}
