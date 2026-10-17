namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class DeclaredResponseEofTests
{
    [Test]
    public Task NativeDeclaredReceiptAndReadAbortPreserveOriginalCommandThenHealthyReplayAndRead()
        => DeclaredResponseEofFlow.RunAsync();
}
