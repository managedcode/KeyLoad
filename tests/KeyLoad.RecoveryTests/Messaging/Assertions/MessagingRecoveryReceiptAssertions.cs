namespace KeyLoad.RecoveryTests.Features.Messaging;

internal static class MessagingRecoveryReceiptAssertions
{
    internal static async Task AssertSameValueAsync(CommitReceipt expected, CommitReceipt actual)
    {
        var expectedBytes = OrleansSerialization.Serialize(expected);
        var actualBytes = OrleansSerialization.Serialize(actual);
        await Assert.That(actualBytes.AsSpan().SequenceEqual(expectedBytes)).IsTrue();
    }
}
