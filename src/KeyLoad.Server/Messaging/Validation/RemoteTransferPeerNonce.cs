namespace KeyLoad.Server.Features.Messaging;

internal static class RemoteTransferPeerNonce
{
    private const string Format = "N";
    private const int Characters = 32;

    internal static bool Valid(string? nonce)
        => nonce is { Length: Characters } && Guid.TryParseExact(nonce, Format, out var value)
            && value != Guid.Empty && string.Equals(nonce, value.ToString(Format), StringComparison.Ordinal);
}
