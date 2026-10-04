using KeyLoad.Core;

namespace KeyLoad.UnitTests.Features.Messaging;

internal static class RemoteTransferTokenTestSupport
{
    private const char SignatureSeparator = '.';

    internal static string TamperClaims<T>(DatabaseEngine database, string originalToken, T changedClaims)
    {
        var changedToken = database.Sign(changedClaims);
        var payloadEnd = changedToken.LastIndexOf(SignatureSeparator);
        var signatureStart = originalToken.LastIndexOf(SignatureSeparator);
        return changedToken[..payloadEnd] + originalToken[signatureStart..];
    }
}
