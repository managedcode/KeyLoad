using KeyLoad.Orleans.Features.ResourceExecution;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal sealed class CacheControlWireDigestTests
{
    [Test]
    public async Task AcCache014DigestRoundTripsAllThirtyTwoBytesWithoutExposingStorage()
    {
        var expectedBytes = DigestBytes();
        var bytes = expectedBytes.ToArray();
        var digest = CacheControlDigest.FromBytes(bytes);
        var restored = new byte[DigestLength];

        digest.WriteBytes(restored);

        await Assert.That(restored.AsSpan().SequenceEqual(expectedBytes)).IsTrue();
        await Assert.That(digest.FixedTimeEquals(CacheControlDigest.FromBytes(bytes))).IsTrue();
        await Assert.That(digest.Word0).IsEqualTo(0x0706050403020100UL);
        await Assert.That(digest.Word1).IsEqualTo(0x0F0E0D0C0B0A0908UL);
        await Assert.That(digest.Word2).IsEqualTo(0x1716151413121110UL);
        await Assert.That(digest.Word3).IsEqualTo(0x1F1E1D1C1B1A1918UL);
        Array.Fill(bytes, byte.MaxValue);
        digest.WriteBytes(restored);
        await Assert.That(restored.AsSpan().SequenceEqual(expectedBytes)).IsTrue();
        for (var index = 0; index < DigestLength; index++)
        {
            await Assert.That(digest.FixedTimeEquals(CacheControlDigest.FromBytes(ChangedByte(expectedBytes, index)))).IsFalse();
        }
    }

    [Test]
    public async Task AcCache014DigestRejectsEveryWrongBufferLength()
    {
        var emptyInput = Assert.ThrowsExactly<ArgumentException>(() => CacheControlDigest.FromBytes([]));
        var shortInput = Assert.ThrowsExactly<ArgumentException>(() => CacheControlDigest.FromBytes(new byte[OneByteShortLength]));
        var longInput = Assert.ThrowsExactly<ArgumentException>(() => CacheControlDigest.FromBytes(new byte[OneByteLongLength]));
        await Assert.That(emptyInput.GetType()).IsEqualTo(typeof(ArgumentException));
        await Assert.That(shortInput.GetType()).IsEqualTo(typeof(ArgumentException));
        await Assert.That(longInput.GetType()).IsEqualTo(typeof(ArgumentException));

        var digest = CacheControlDigest.FromBytes(DigestBytes());
        var emptyOutput = Assert.ThrowsExactly<ArgumentException>(() => digest.WriteBytes(Array.Empty<byte>()));
        var shortOutput = Assert.ThrowsExactly<ArgumentException>(() => digest.WriteBytes(new byte[OneByteShortLength]));
        var longOutput = Assert.ThrowsExactly<ArgumentException>(() => digest.WriteBytes(new byte[OneByteLongLength]));
        await Assert.That(emptyOutput.GetType()).IsEqualTo(typeof(ArgumentException));
        await Assert.That(shortOutput.GetType()).IsEqualTo(typeof(ArgumentException));
        await Assert.That(longOutput.GetType()).IsEqualTo(typeof(ArgumentException));
    }

    private static byte[] DigestBytes()
    =>
    [
        0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07,
        0x08, 0x09, 0x0A, 0x0B, 0x0C, 0x0D, 0x0E, 0x0F,
        0x10, 0x11, 0x12, 0x13, 0x14, 0x15, 0x16, 0x17,
        0x18, 0x19, 0x1A, 0x1B, 0x1C, 0x1D, 0x1E, 0x1F
    ];

    private static byte[] ChangedByte(byte[] original, int index)
    {
        var changed = original.ToArray();
        changed[index] ^= 0x80;
        return changed;
    }

    private const int DigestLength = 32;
    private const int OneByteShortLength = DigestLength - 1;
    private const int OneByteLongLength = DigestLength + 1;
}
