using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class KeyCodecGoldenContractTests
{
    [Test]
    public async Task EveryFrozenV1TagRetainsItsLiteralBytes()
    {
        await AssertHexAsync("0110", KeyCodec.Encode(MissingValue.Instance));
        await AssertHexAsync("0111", KeyCodec.Encode((object?)null));
        await AssertHexAsync("012000", KeyCodec.Encode(false));
        await AssertHexAsync("012001", KeyCodec.Encode(true));
        await AssertHexAsync("01308000000000000000", KeyCodec.Encode(0L));
        await AssertHexAsync("013101", KeyCodec.Encode(0m));
        await AssertHexAsync("01328000000000000000", KeyCodec.Encode(0d));
        await AssertHexAsync("01408000000000000000", KeyCodec.Encode(DateTimeOffset.MinValue));
        await AssertHexAsync("0150610000", KeyCodec.Encode("a"));
        await AssertHexAsync("0160610000", KeyCodec.Encode(new byte[] { (byte)'a' }));
    }

    [Test]
    public async Task ConvenienceTypesNormalizeWithoutChangingTheirV1Representation()
    {
        var id = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        await Assert.That(KeyCodec.Encode(id).SequenceEqual(KeyCodec.Encode(id.ToString("N")))).IsTrue();
        await Assert.That(KeyCodec.Encode(7).SequenceEqual(KeyCodec.Encode(7L))).IsTrue();
        await Assert.That(KeyCodec.Encode(1.00m).SequenceEqual(KeyCodec.Encode(1m))).IsTrue();
        await Assert.That(KeyCodec.Encode(-0d).SequenceEqual(KeyCodec.Encode(0d))).IsTrue();

        var withOffset = new DateTimeOffset(2024, 2, 3, 4, 5, 6, TimeSpan.FromHours(2));
        await Assert.That(KeyCodec.Encode(withOffset).SequenceEqual(KeyCodec.Encode(withOffset.ToUniversalTime()))).IsTrue();
    }

    private static async Task AssertHexAsync(string expected, byte[] actual)
        => await Assert.That(Convert.ToHexString(actual)).IsEqualTo(expected);
}
