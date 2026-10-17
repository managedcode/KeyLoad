namespace KeyLoad.UnitTests.Features.StorageRecovery;

internal static class KeyCodecExceptionAssertions
{
    internal static async Task AssertAsync(Action action, ErrorCode code, string message)
    {
        var failure = Assert.ThrowsExactly<KeyLoadException>(action);
        await Assert.That(failure.Code).IsEqualTo(code);
        await Assert.That(failure.Message).IsEqualTo(message);
    }
}
