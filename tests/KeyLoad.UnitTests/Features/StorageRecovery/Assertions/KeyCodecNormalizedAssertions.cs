namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class KeyCodecNormalizedAssertions
{
    internal static async Task AssertEquivalentAsync(object? expected, object? actual)
    {
        if (expected is byte[] expectedBytes)
        {
            await Assert.That(actual).IsTypeOf<byte[]>();
            await Assert.That(((byte[])actual!).SequenceEqual(expectedBytes)).IsTrue();
            return;
        }

        if (expected is DateTimeOffset expectedTime)
        {
            await Assert.That(actual).IsTypeOf<DateTimeOffset>();
            var actualTime = (DateTimeOffset)actual!;
            await Assert.That(actualTime.UtcTicks).IsEqualTo(expectedTime.UtcTicks);
            await Assert.That(actualTime.Offset).IsEqualTo(TimeSpan.Zero);
            return;
        }

        if (expected is decimal expectedDecimal)
        {
            await Assert.That(actual).IsTypeOf<decimal>();
            await Assert.That((decimal)actual!).IsEqualTo(expectedDecimal);
            return;
        }

        await Assert.That(actual).IsEqualTo(expected);
    }
}
