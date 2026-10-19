namespace KeyLoad.UnitTests.Features.ClientApi;

internal sealed class DeclaredResponseEofTests
{
    [Test]
    public Task DeclaredReceiptAndReadAbortPreserveOriginalCommandThenHealthyReplayAndRead()
        => DeclaredResponseEofFlow.RunAsync();
}
