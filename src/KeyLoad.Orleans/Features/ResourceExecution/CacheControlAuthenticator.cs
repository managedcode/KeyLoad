using System.Security.Cryptography;

namespace KeyLoad.Orleans.Features.ResourceExecution;

internal sealed class CacheControlAuthenticator(ReadOnlySpan<byte> peerKey, CacheControlDigest trustedScope) : IDisposable
{
    private readonly object gate = new();
    private readonly CacheControlDigest scope = trustedScope;
    private readonly byte[] key = CacheControlKey.Derive(peerKey, trustedScope);
    private bool disposed;

    internal bool TrySign(CacheReadyProof? message, out CacheReadyProof? signed)
        => TrySignCore(message, out signed);

    internal bool TrySign(CachePrepareRequest? message, out CachePrepareRequest? signed)
        => TrySignCore(message, out signed);

    internal bool TrySign(CacheGrantRequest? message, out CacheGrantRequest? signed)
        => TrySignCore(message, out signed);

    internal bool TrySign(CacheRevokeRequest? message, out CacheRevokeRequest? signed)
        => TrySignCore(message, out signed);

    internal bool TrySign(CacheRefreshHint? message, out CacheRefreshHint? signed)
        => TrySignCore(message, out signed);

    internal bool TrySign(CachePrepareReply? message, out CachePrepareReply? signed)
        => TrySignCore(message, out signed);

    internal bool TrySign(CacheGrantReply? message, out CacheGrantReply? signed)
        => TrySignCore(message, out signed);

    internal bool TrySign(CacheRevokeReply? message, out CacheRevokeReply? signed)
        => TrySignCore(message, out signed);

    internal bool TrySign(CacheRefreshReceipt? message, out CacheRefreshReceipt? signed)
        => TrySignCore(message, out signed);

    internal bool TryAuthenticate(ICacheControlMessage? message)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return TryTranscript(message, out var transcript) && !message!.Mac.FixedTimeEquals(default)
                && message.Mac.FixedTimeEquals(CacheControlMac.Compute(key, transcript));
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            CryptographicOperations.ZeroMemory(key);
        }
    }

    private bool TrySignCore<T>(T? message, out T? signed) where T : class, ICacheControlMessage
    {
        signed = null;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!TryTranscript(message, out var transcript))
            {
                return false;
            }

            signed = (T)CacheControlMac.Replace(message!, CacheControlMac.Compute(key, transcript));
            return true;
        }
    }

    private bool TryTranscript(ICacheControlMessage? message, out byte[] transcript)
    {
        transcript = [];
        if (!CacheControlWire.TryEncodeForSigning(message, out var encoded)
            || !scope.FixedTimeEquals(CacheControlShape.Scope(message!))
            || !CacheControlNestedAuthentication.Verify(key, scope, message!))
        {
            return false;
        }

        transcript = encoded;
        return true;
    }
}
