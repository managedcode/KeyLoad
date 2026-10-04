using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class KeyCodecTests
{
    private const int ExpectedAdditionalValues = 6;
    private const decimal MinimumPositiveDecimal = 0.0000000000000000000000000001m;

    [Test]
    public async Task GoldenKeysRemainStable()
    {
        await Assert.That(Convert.ToHexString(KeyCodec.Encode((object?)null))).IsEqualTo("0111");
        await Assert.That(Convert.ToHexString(KeyCodec.Encode(0L))).IsEqualTo("01308000000000000000");
        await Assert.That(Convert.ToHexString(KeyCodec.Encode("a"))).IsEqualTo("0150610000");
        await Assert.That(KeyCodec.Encode(MissingValue.Instance)).IsNotEquivalentTo(KeyCodec.Encode((object?)null), CollectionOrdering.Matching);
    }

    [Test]
    public async Task TenThousandSeededDecimalKeysRoundTripAndSortNumerically()
    {
        var corpus = KeyCodecCorpus.Create();
        var repeatedCorpus = KeyCodecCorpus.Create();
        await Assert.That(corpus.Length).IsEqualTo(KeyCodecCorpus.ValueCount);
        await Assert.That(corpus.SequenceEqual(repeatedCorpus)).IsTrue();
        await Assert.That(corpus.Any(value => decimal.GetBits(value)[3] < 0)).IsTrue();
        await Assert.That(corpus.Any(value => decimal.GetBits(value)[3] >= 0)).IsTrue();
        await Assert.That(Enumerable.Range(0, KeyCodecCorpus.ScaleCount).All(scale =>
            corpus.Any(value => ((decimal.GetBits(value)[3] >> KeyCodecCorpus.DecimalScaleBitShift)
                & KeyCodecCorpus.DecimalScaleMask) == scale))).IsTrue();

        var numbers = corpus.Concat([decimal.MinValue, decimal.MaxValue, 0, -1, 1, MinimumPositiveDecimal]).ToArray();
        await Assert.That(numbers.Length).IsEqualTo(KeyCodecCorpus.ValueCount + ExpectedAdditionalValues);
        foreach (var number in numbers)
        {
            var decoded = KeyCodec.Decode(KeyCodec.Encode(number));
            var decodedValue = System.Linq.Enumerable.Single(decoded);
            await Assert.That(decodedValue).IsNotNull();
            await Assert.That(decodedValue).IsTypeOf<decimal>();
            await Assert.That((decimal)decodedValue!).IsEqualTo(number);
        }

        var encodedOrder = numbers.OrderBy(number => KeyCodec.Encode(number), BinaryKeyComparer.Instance).ToArray();
        await Assert.That(encodedOrder).IsEquivalentTo(numbers.Order().ToArray(), CollectionOrdering.Matching);
    }

    [Test]
    public async Task StringsUseUtf8OrdinalOrderingAndEscapedComponentsDoNotCollide()
    {
        var samples = new[] { "", "a", "a\0", "a\0b", "aa", "b", "é", "ї", "😀", "\uffff" };
        foreach (var value in samples)
        {
            await Assert.That(KeyCodec.Decode(KeyCodec.Encode(value))[0]).IsEqualTo(value);
        }

        var expected = samples.OrderBy(System.Text.Encoding.UTF8.GetBytes, BinaryKeyComparer.Instance).ToArray();
        await Assert.That(samples.OrderBy(value => KeyCodec.Encode(value), BinaryKeyComparer.Instance).ToArray())
            .IsEquivalentTo(expected, CollectionOrdering.Matching);
        await Assert.That(KeyCodec.Encode("a", "bc")).IsNotEquivalentTo(KeyCodec.Encode("ab", "c"), CollectionOrdering.Matching);
        await Assert.That(BinaryKeyComparer.Instance.Compare(KeyCodec.Encode(1.00m), KeyCodec.Encode(1m))).IsEqualTo(0);
    }

    [Test]
    public async Task Int64AndFiniteDoublesPreserveOrder()
    {
        var integers = new[] { long.MinValue, -1L, 0L, 1L, long.MaxValue };
        var values = new[] { double.MinValue, -1e100, -1.0, -double.Epsilon, 0.0, double.Epsilon, 1.0, double.MaxValue };
        await Assert.That(integers.OrderBy(value => KeyCodec.Encode(value), BinaryKeyComparer.Instance))
            .IsEquivalentTo(integers, CollectionOrdering.Matching);
        await Assert.That(values.OrderBy(value => KeyCodec.Encode(value), BinaryKeyComparer.Instance))
            .IsEquivalentTo(values, CollectionOrdering.Matching);
        Assert.ThrowsExactly<KeyLoadException>(() => KeyCodec.Encode(double.NaN));
        Assert.ThrowsExactly<KeyLoadException>(() => KeyCodec.Decode([1, 0xff]));
    }
}
