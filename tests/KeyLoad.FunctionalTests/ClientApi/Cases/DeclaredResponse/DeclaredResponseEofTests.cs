namespace KeyLoad.FunctionalTests.ClientApi;

internal sealed class DeclaredResponseEofTests
{
    [Test]
    public Task DeclaredReceiptAndReadAbortPreserveOriginalCommandThenHealthyReplayAndRead()
        => DeclaredResponseEofFlow.RunAsync();
}
