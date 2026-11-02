using System.Collections;
using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal sealed class DecodedKeyCollectionTests
{
    [Test]
    public async Task EmptyKeysRetainCollectionAndEnumeratorBounds()
    {
        var decoded = KeyCodec.Decode([0x01]);
        await Assert.That(decoded.Length).IsEqualTo(0);
        await Assert.That(default(DecodedKey).Count).IsEqualTo(0);
        await Assert.That(decoded.ToArray()).IsEmpty();
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = decoded[-1]);
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = decoded[0]);
        _ = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = default(DecodedKey)[0]);
        var enumerator = decoded.GetEnumerator();
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => _ = enumerator.Current);
        await Assert.That(enumerator.MoveNext()).IsFalse();
        await Assert.That(enumerator.MoveNext()).IsFalse();
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => _ = enumerator.Current);
        _ = Assert.ThrowsExactly<NotSupportedException>(enumerator.Reset);
        enumerator.Dispose();
    }

    [Test]
    public async Task TypedAndClrEnumerationRetainAllScalarValuesAndCanonicalBytes()
    {
        KeyComponent[] expected = ["α", long.MinValue, 1.25m, true, new byte[] { 0, 255 }];
        var bytes = KeyCodec.Encode(expected);
        var decoded = KeyCodec.Decode(bytes);
        await Assert.That(decoded.Count).IsEqualTo(expected.Length);
        await Assert.That(decoded.SequenceEqual(expected)).IsTrue();
        var untyped = ((IEnumerable)decoded).GetEnumerator();
        foreach (var component in expected)
        {
            await Assert.That(untyped.MoveNext()).IsTrue();
            await Assert.That((KeyComponent)untyped.Current).IsEqualTo(component);
        }
        await Assert.That(untyped.MoveNext()).IsFalse();
        _ = Assert.ThrowsExactly<InvalidOperationException>(() => _ = untyped.Current);
        ((IDisposable)untyped).Dispose();
        await Assert.That(KeyCodec.Encode(decoded).AsSpan().SequenceEqual(bytes)).IsTrue();
    }
}
