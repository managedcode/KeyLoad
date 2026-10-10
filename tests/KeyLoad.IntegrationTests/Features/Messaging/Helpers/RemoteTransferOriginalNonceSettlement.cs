namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class RemoteTransferOriginalNonceSettlement
{
    internal static async Task<KeyLoadException> ReadAsync(Task original)
    {
        try
        { await original.ConfigureAwait(false); }
        catch (KeyLoadException error) when (error.Code == ErrorCode.Unauthenticated) { return error; }
        throw new InvalidOperationException(RemoteTransferDistinctProtocol.Missing);
    }
}
