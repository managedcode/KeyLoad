using System.Globalization;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal sealed class KeyCodecCultureContractTests
{
    private const string CustomNegativeSign = "−";
    private const string PositiveFractionGolden = "0131023F0200";
    private const string NegativeFractionGolden = "013100C0FDFF";
    private const string SmallestFractionGolden = "013102250200";

    [Test]
    public async Task DecimalV1GoldensDecodeAndEncodeIndependentOfCurrentCulture()
    {
        var originalCulture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            await AssertLiteralVectorsAsync();

            var customCulture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            customCulture.NumberFormat.NegativeSign = CustomNegativeSign;
            CultureInfo.CurrentCulture = customCulture;
            await AssertLiteralVectorsAsync();
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }
    }

    private static async Task AssertLiteralVectorsAsync()
    {
        await Assert.That(KeyCodec.Decode(Convert.FromHexString(PositiveFractionGolden))[0]).IsEqualTo(0.01m);
        await Assert.That(KeyCodec.Decode(Convert.FromHexString(NegativeFractionGolden))[0]).IsEqualTo(-0.01m);
        await Assert.That(KeyCodec.Decode(Convert.FromHexString(SmallestFractionGolden))[0]).IsEqualTo(0.0000000000000000000000000001m);

        await Assert.That(Convert.ToHexString(KeyCodec.Encode(0.01m))).IsEqualTo(PositiveFractionGolden);
        await Assert.That(Convert.ToHexString(KeyCodec.Encode(-0.01m))).IsEqualTo(NegativeFractionGolden);
        await Assert.That(Convert.ToHexString(KeyCodec.Encode(0.0000000000000000000000000001m))).IsEqualTo(SmallestFractionGolden);
    }
}
