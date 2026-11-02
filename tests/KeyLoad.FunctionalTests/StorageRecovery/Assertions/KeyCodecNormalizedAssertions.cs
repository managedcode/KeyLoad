using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.StorageRecovery;

internal static class KeyCodecNormalizedAssertions
{
    internal static async Task AssertEquivalentAsync(object? expected, KeyComponent actual)
    {
        switch (expected)
        {
            case null:
                await Assert.That(actual.Kind).IsEqualTo(KeyComponentKind.Null);
                break;
            case MissingValue:
                await Assert.That(actual.Kind).IsEqualTo(KeyComponentKind.Missing);
                break;
            case bool flag:
                await Assert.That(actual.Kind).IsEqualTo(KeyComponentKind.Boolean);
                await Assert.That(actual.Boolean).IsEqualTo(flag);
                break;
            case long integer:
                await Assert.That(actual.Kind).IsEqualTo(KeyComponentKind.WholeNumber);
                await Assert.That(actual.Integer).IsEqualTo(integer);
                break;
            case decimal number:
                await Assert.That(actual.Kind).IsEqualTo(KeyComponentKind.FixedPoint);
                await Assert.That(actual.ExactNumber).IsEqualTo(number);
                break;
            case double number:
                await Assert.That(actual.Kind).IsEqualTo(KeyComponentKind.FloatingPoint);
                await Assert.That(actual.RealNumber).IsEqualTo(number);
                break;
            case DateTimeOffset time:
                await Assert.That(actual.Kind).IsEqualTo(KeyComponentKind.Timestamp);
                await Assert.That(actual.Timestamp.UtcTicks).IsEqualTo(time.UtcTicks);
                await Assert.That(actual.Timestamp.Offset).IsEqualTo(TimeSpan.Zero);
                break;
            case string text:
                await Assert.That(actual.Kind).IsEqualTo(KeyComponentKind.Text);
                await Assert.That(actual.Text).IsEqualTo(text);
                break;
            case byte[] bytes:
                await Assert.That(actual.Kind).IsEqualTo(KeyComponentKind.Binary);
                await Assert.That(actual.Bytes.Span.SequenceEqual(bytes)).IsTrue();
                break;
            default:
                throw new InvalidOperationException("The independent key oracle contains an unsupported CLR value.");
        }
    }
}
