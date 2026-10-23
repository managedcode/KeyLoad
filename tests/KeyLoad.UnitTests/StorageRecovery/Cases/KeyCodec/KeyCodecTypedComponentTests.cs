using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

/// <summary>AC-KEYCODEC-001: typed components encode to exactly the bytes of the boxed components they replace.</summary>
internal sealed class KeyCodecTypedComponentTests
{
    private const string Text = "tenant/\u0000zero☃";
    private const long Integer = -42;
    private const int Small = 7;
    private const double Real = 2.5;
    private const int Year = 2026;
    private const int Month = 10;
    private const int Day = 24;
    private const int LongTextLength = 3_000;
    private const int MaximumComponents = 256;
    private static readonly byte[] Binary = [0, 1, 0, 255];

    [Test]
    public async Task TypedComponentsMatchBoxedBytesForEverySupportedShape()
    {
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(Year, Month, Day, 0, 0, 0, TimeSpan.Zero);
        var typed = KeyCodec.Encode(Text, Integer, Small, true, false, 1.25m, Real, at, id, Binary, KeyComponent.Null, KeyComponent.Missing);
        var boxed = KeyCodec.Encode(new object?[] { Text, Integer, Small, true, false, 1.25m, Real, at, id, Binary, null, MissingValue.Instance });
        await Assert.That(typed.SequenceEqual(boxed)).IsTrue();
        var decoded = KeyCodec.Decode(typed);
        await Assert.That(decoded[0]).IsEqualTo(Text);
        await Assert.That(decoded[1]).IsEqualTo(Integer);
        await Assert.That(decoded[2]).IsEqualTo((long)Small);
        await Assert.That(decoded[8]).IsEqualTo(id.ToString("N"));
        await Assert.That(decoded[10]).IsNull();
        await Assert.That(decoded[11]).IsEqualTo(MissingValue.Instance);
    }

    [Test]
    public async Task LongTextGrowsPastTheStackBufferAndStillMatchesBoxedBytes()
    {
        var text = new string('é', LongTextLength);
        var typed = KeyCodec.Encode(text, text);
        await Assert.That(typed.SequenceEqual(KeyCodec.Encode([text, text]))).IsTrue();
        await Assert.That(KeyCodec.Decode(typed)[1]).IsEqualTo(text);
    }

    [Test]
    public async Task NullReferencesAndComponentBoundsAreTheSameInBothOverloads()
    {
        string? missingText = null;
        byte[]? missingBytes = null;
        await Assert.That(KeyCodec.Encode(missingText, missingBytes).SequenceEqual(KeyCodec.Encode(new object?[] { null, null }))).IsTrue();
        var tooMany = new KeyComponent[MaximumComponents + 1];
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => KeyCodec.Encode(tooMany));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        var nonFinite = Assert.ThrowsExactly<KeyLoadException>(() => KeyCodec.Encode(double.NaN));
        await Assert.That(nonFinite.Code).IsEqualTo(ErrorCode.Validation);
    }
}
