using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace KeyLoad.Server.Features.EventStreams;

internal sealed class EventVectorAuthenticatedExchange : IDisposable
{
    private readonly byte[] originalRequest;
    private EventVectorMacCheckedReply? reply;
    private bool disposed;
    private string? requestSignature;

    internal EventVectorAuthenticatedExchange(byte[] originalRequest, string requestSignature,
        EventVectorMacCheckedReply originalReply)
    {
        ArgumentNullException.ThrowIfNull(originalRequest);
        ArgumentNullException.ThrowIfNull(originalReply);
        this.originalRequest = originalRequest;
        ArgumentNullException.ThrowIfNull(requestSignature);
        this.requestSignature = requestSignature;
        reply = originalReply;
    }

    internal ReadOnlyMemory<byte> OriginalRequestBytes
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return originalRequest;
        }
    }

    internal string OriginalRequestSignature
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return requestSignature ?? throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid);
        }
    }

    internal EventVectorMacCheckedReply Reply
    {
        get
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return reply ?? throw Errors.Fail(ErrorCode.Corruption, EventVectorRemoteProtocol.Invalid);
        }
    }

    public void Dispose()
    {
        if (disposed)
        {
            return;
        }
        disposed = true;
        CryptographicOperations.ZeroMemory(originalRequest);
        if (reply is { } owned)
        {
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(owned.OriginalReplyBytes).Span);
            CryptographicOperations.ZeroMemory(MemoryMarshal.AsMemory(owned.Reply.OriginalNativeReply).Span);
        }
        reply = null;
        requestSignature = null;
    }
}
