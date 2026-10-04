using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class KeyCodecInputContractTests
{
    [Test]
    public async Task ZeroOneAndMaximumComponentCountsAreSupported()
    {
        var empty = KeyCodec.Encode();
        await Assert.That(empty.SequenceEqual(new byte[] { 1 })).IsTrue();
        await Assert.That(KeyCodec.Decode(empty).Length).IsEqualTo(0);

        var one = KeyCodec.Decode(KeyCodec.Encode("one"));
        await Assert.That(one.Length).IsEqualTo(1);
        await KeyCodecNormalizedAssertions.AssertEquivalentAsync("one", one[0]);

        var maximum = Enumerable.Repeat<object?>(null, 256).ToArray();
        await Assert.That(KeyCodec.Decode(KeyCodec.Encode(maximum)).Length).IsEqualTo(256);
    }

    [Test]
    public async Task ExcessArityFailsBeforeEncodingUnsupportedFinalComponent()
    {
        var values = Enumerable.Repeat<object?>(null, 256).Append(new object()).ToArray();
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => KeyCodec.Encode(values));

        await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        await Assert.That(failure.Message).IsEqualTo("Key component count exceeds the supported limit.");
    }

    [Test]
    public async Task ExcessPersistedArityDoesNotLeakFinalTimestampOverflow()
    {
        var encoded = new byte[1 + 256 + 9];
        encoded[0] = 1;
        Array.Fill(encoded, (byte)0x11, 1, 256);
        Convert.FromHexString("40ABCA2875F4374000").CopyTo(encoded, 257);

        var failure = Assert.ThrowsExactly<KeyLoadException>(() => KeyCodec.Decode(encoded));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        await Assert.That(failure.Message).IsEqualTo("Invalid encoded key.");
    }

    [Test]
    public async Task UnsupportedAndInvalidClrValuesUseFrozenTypedErrors()
    {
        await KeyCodecExceptionAssertions.AssertAsync(() => KeyCodec.Encode(new DateTime(2024, 1, 1)),
            ErrorCode.UnsupportedCapability, "This type is not supported by key codec v1.");
        await KeyCodecExceptionAssertions.AssertAsync(() => KeyCodec.Encode(double.PositiveInfinity),
            ErrorCode.Validation, "Indexed numbers must be finite.");
        await KeyCodecExceptionAssertions.AssertAsync(() => KeyCodec.Encode("invalid-\uD800"),
            ErrorCode.Validation, "Key text is not valid Unicode.");
    }
}
