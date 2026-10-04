using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class KeyCodecMalformedContractTests
{
    [Test]
    public async Task LiteralMalformedTagScalarEscapeTextAndTimestampVectorsFailClosed()
    {
        var vectors = new[]
        {
            "01FE", "012002", "0120", "0130", "0131", "013102", "0132", "0140",
            "0150610001", "01506100", "0150C0800000", "0150C30000",
            "01327FFFFFFFFFFFFFFF", "0140ABCA2875F4374000"
        };
        foreach (var vector in vectors)
        {
            await KeyCodecExceptionAssertions.AssertAsync(() => KeyCodec.Decode(Convert.FromHexString(vector)),
                ErrorCode.Corruption, "Invalid encoded key.");
        }
    }

    [Test]
    public async Task LiteralNoncanonicalAndUnrepresentableDecimalVectorsFailClosed()
    {
        var vectors = new[]
        {
            "013103",                 // Unknown sign.
            "0131024000",             // Nonzero form without digits.
            "013102400100",           // Zero encoded in nonzero form.
            "01310242020100",         // Noncanonical trailing zero.
            "01310242010200",         // Noncanonical leading zero.
            "013102240200",           // 1e-29 underflows decimal scale.
            "0131023F02030405060708090A0102030405060708090A01020304050607080900", // Requires rounding.
            "0131025D080A030309020703060205030705040408060A040605040A060104040700" // Decimal max + 1.
        };
        foreach (var vector in vectors)
        {
            await KeyCodecExceptionAssertions.AssertAsync(() => KeyCodec.Decode(Convert.FromHexString(vector)),
                ErrorCode.Corruption, "Invalid encoded key.");
        }
    }

    [Test]
    public async Task EmptyAndUnknownVersionsRemainFormatUnsupported()
    {
        await KeyCodecExceptionAssertions.AssertAsync(() => KeyCodec.Decode([]),
            ErrorCode.FormatUnsupported, "Unknown key codec version.");
        await KeyCodecExceptionAssertions.AssertAsync(() => KeyCodec.Decode([2]),
            ErrorCode.FormatUnsupported, "Unknown key codec version.");
    }

    [Test]
    public async Task ValidUnicodeEscapesAndNumericExtremaRemainReadable()
    {
        var values = new object?[] { "a\0b", "é", "ї", "😀", long.MinValue, long.MaxValue,
            decimal.MinValue, decimal.MaxValue, double.Epsilon, double.MaxValue, DateTimeOffset.MaxValue };
        var decoded = KeyCodec.Decode(KeyCodec.Encode(values));

        await Assert.That(decoded.Length).IsEqualTo(values.Length);
        for (var index = 0; index < values.Length; index++)
        {
            await KeyCodecNormalizedAssertions.AssertEquivalentAsync(
                KeyCodecMixedCorpus.Normalize(values[index]), decoded[index]);
        }
    }
}
