using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.StorageRecovery;

/// <summary>AC-KEYCODEC-001: closed scalar components preserve the canonical bytes and decoded values.</summary>
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
    public async Task TypedComponentsPreserveEverySupportedDecodedShape()
    {
        var id = Guid.NewGuid();
        var at = new DateTimeOffset(Year, Month, Day, 0, 0, 0, TimeSpan.Zero);
        var typed = KeyCodec.Encode(Text, Integer, Small, true, false, 1.25m, Real, at, id, Binary, KeyComponent.Null, KeyComponent.Missing);
        KeyCodecValidation.Validate(typed);
        var decoded = KeyCodec.Decode(typed);
        await Assert.That(decoded[0].Text).IsEqualTo(Text);
        await Assert.That(decoded[1].WholeNumber).IsEqualTo(Integer);
        await Assert.That(decoded[2].WholeNumber).IsEqualTo((long)Small);
        await Assert.That(decoded[3].Boolean).IsTrue();
        await Assert.That(decoded[4].Boolean).IsFalse();
        await Assert.That(decoded[5].ExactNumber).IsEqualTo(1.25m);
        await Assert.That(decoded[6].RealNumber).IsEqualTo(Real);
        await Assert.That(decoded[7].Timestamp).IsEqualTo(at);
        await Assert.That(decoded[9].Bytes.Span.SequenceEqual(Binary)).IsTrue();
        await Assert.That(decoded[8].Text).IsEqualTo(id.ToString("N"));
        await Assert.That(decoded[10].Kind).IsEqualTo(KeyComponentKind.Null);
        await Assert.That(decoded[11].Kind).IsEqualTo(KeyComponentKind.Missing);
    }

    [Test]
    public async Task LongTextGrowsPastTheStackBufferAndKeepsLiteralUtf8Bytes()
    {
        var text = new string('é', LongTextLength);
        var typed = KeyCodec.Encode(text, text);
        var scalar = string.Concat(Enumerable.Repeat("C3A9", LongTextLength));
        await Assert.That(Convert.ToHexString(typed)).IsEqualTo("0150" + scalar + "000050" + scalar + "0000");
        await Assert.That(KeyCodec.Decode(typed)[1].Text).IsEqualTo(text);
    }

    [Test]
    public async Task NullReferencesAndComponentBoundsKeepTheirCanonicalContract()
    {
        string? missingText = null;
        byte[]? missingBytes = null;
        await Assert.That(Convert.ToHexString(KeyCodec.Encode(missingText, missingBytes))).IsEqualTo("011111");
        var tooMany = new KeyComponent[MaximumComponents + 1];
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => KeyCodec.Encode(tooMany));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.ResourceExhausted);
        var nonFinite = Assert.ThrowsExactly<KeyLoadException>(() => KeyCodec.Encode(double.NaN));
        await Assert.That(nonFinite.Code).IsEqualTo(ErrorCode.Validation);
    }

    [Test]
    public async Task ValidationKeepsTheCompleteComponentLimitWithoutMaterializingText()
    {
        var valid = KeyCodec.Encode(Enumerable.Repeat(KeyComponent.Missing, MaximumComponents).ToArray());
        KeyCodecValidation.Validate(valid);
        await Assert.That(KeyCodec.Decode(valid).Length).IsEqualTo(MaximumComponents);
        var excessive = valid.Concat(new byte[] { 0x10 }).ToArray();
        var failure = Assert.ThrowsExactly<KeyLoadException>(() => KeyCodecValidation.Validate(excessive));
        await Assert.That(failure.Code).IsEqualTo(ErrorCode.Corruption);
        KeyCodecValidation.Validate(KeyCodec.Encode(Text, Binary, 1.25m, DateTimeOffset.MaxValue));
    }
}
