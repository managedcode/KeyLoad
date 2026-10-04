namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class MessagingRecoveryReceiptAssertions
{
    internal static async Task AssertSameNativeValueAsync(CommitReceipt expected, CommitReceipt actual)
    {
        var expectedBytes = NativeSerialization.Serialize(expected);
        var actualBytes = NativeSerialization.Serialize(actual);
        await Assert.That(actualBytes.AsSpan().SequenceEqual(expectedBytes)).IsTrue();
    }
}
