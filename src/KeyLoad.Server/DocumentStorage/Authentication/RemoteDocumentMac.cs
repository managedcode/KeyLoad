using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class RemoteDocumentMac : IDisposable
{
    private static readonly byte[] RequestDomain = Encoding.ASCII.GetBytes(RemoteDocumentProtocol.RequestDomain);
    private static readonly byte[] ReplyDomain = Encoding.ASCII.GetBytes(RemoteDocumentProtocol.ReplyDomain);
    private const int ActiveState = 0;
    private const int DisposedState = 1;
    private static readonly byte[] NativeRequestDomain = Encoding.ASCII.GetBytes(RemoteModelNativeProtocol.NativeRequestPurpose);
    private static readonly byte[] NativeReplyDomain = Encoding.ASCII.GetBytes(RemoteModelNativeProtocol.NativeReplyPurpose);
    private readonly byte[] key;
    private readonly Lock lifecycle = new();
    private int disposed;

    internal RemoteDocumentMac(ReadOnlySpan<byte> key)
    {
        if (key.Length != RemoteDocumentProtocol.SecretBytes)
        {
            throw Errors.Fail(ErrorCode.Validation, RemoteDocumentProtocol.InvalidProof);
        }
        this.key = key.ToArray();
    }

    internal static RemoteDocumentMac FromConfiguredSecret(string? secret)
    {
        if (secret is null)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
        var key = Convert.FromBase64String(secret);
        try
        {
            return new(key);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(key);
        }
    }

    internal string Sign(ReadOnlySpan<byte> body, bool reply)
        => Convert.ToHexStringLower(Digest(body, reply));

    internal bool Verify(ReadOnlySpan<byte> body, string signature, bool reply)
    {
        RequireOpen();
        if (signature.Length != RemoteDocumentProtocol.SignatureCharacters
            || signature.Any(character => !char.IsAsciiHexDigitLower(character)))
        {
            return false;
        }
        var supplied = Convert.FromHexString(signature);
        var expected = Digest(body, reply);
        return CryptographicOperations.FixedTimeEquals(supplied, expected);
    }

    internal string SignNativeRequest(RemoteModelNativeRequest request)
    {
        RequireOpen();
        ArgumentNullException.ThrowIfNull(request);
        return Convert.ToHexStringLower(NativeDigest(request with
        {
            SenderSignature = string.Empty
        },
            RemoteModelNativeProtocol.NativeRequestPurpose, NativeRequestDomain));
    }

    internal bool VerifyNativeRequest(RemoteModelNativeRequest request)
    {
        RequireOpen();
        ArgumentNullException.ThrowIfNull(request);
        if (!NativeSignature(request.SenderSignature))
        {
            return false;
        }
        var expected = NativeDigest(request with
        {
            SenderSignature = string.Empty
        },
            RemoteModelNativeProtocol.NativeRequestPurpose, NativeRequestDomain);
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(request.SenderSignature), expected);
    }

    internal string SignNativeReply(RemoteModelNativeReply reply)
    {
        RequireOpen();
        ArgumentNullException.ThrowIfNull(reply);
        return Convert.ToHexStringLower(NativeDigest(reply with
        {
            SenderSignature = string.Empty
        },
            RemoteModelNativeProtocol.NativeReplyPurpose, NativeReplyDomain));
    }

    internal bool VerifyNativeReply(RemoteModelNativeReply reply)
    {
        RequireOpen();
        ArgumentNullException.ThrowIfNull(reply);
        if (!NativeSignature(reply.SenderSignature))
        {
            return false;
        }
        var expected = NativeDigest(reply with
        {
            SenderSignature = string.Empty
        },
            RemoteModelNativeProtocol.NativeReplyPurpose, NativeReplyDomain);
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(reply.SenderSignature), expected);
    }

    private byte[] NativeDigest<T>(T unsigned, string purpose, ReadOnlySpan<byte> domain)
    {
        if (NativeSerialization.Measure(unsigned) > RemoteModelNativeProtocol.MaximumEnvelopeBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteModelNativeProtocol.InvalidProof);
        }
        var bytes = NativeSerialization.Serialize(unsigned);
        if (bytes.Length > RemoteModelNativeProtocol.MaximumEnvelopeBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteModelNativeProtocol.InvalidProof);
        }
        var frame = new RemoteModelNativeMacFrame(purpose, SHA256.HashData(bytes));
        if (NativeSerialization.Measure(frame) > RemoteModelNativeProtocol.MaximumFrameBytes)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteModelNativeProtocol.InvalidProof);
        }
        return Digest(NativeSerialization.Serialize(frame), domain, RemoteModelNativeProtocol.MaximumFrameBytes);
    }

    private static bool NativeSignature(string? signature)
        => signature is { Length: RemoteDocumentProtocol.SignatureCharacters }
            && signature.All(char.IsAsciiHexDigitLower);

    private byte[] Digest(ReadOnlySpan<byte> body, bool reply)
        => Digest(body, reply ? ReplyDomain : RequestDomain, RemoteDocumentProtocol.MaximumBodyBytes);

    private byte[] Digest(ReadOnlySpan<byte> body, ReadOnlySpan<byte> domain, int maximum)
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed != ActiveState, this);
            if (body.IsEmpty || body.Length > maximum)
            {
                throw Errors.Fail(ErrorCode.ResourceExhausted, RemoteDocumentProtocol.InvalidProof);
            }
            using var hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
            hash.AppendData(domain);
            hash.AppendData(body);
            return hash.GetHashAndReset();
        }
    }

    private void RequireOpen()
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(disposed != ActiveState, this);
        }
    }

    public void Dispose()
    {
        lock (lifecycle)
        {
            if (disposed == DisposedState)
            {
                return;
            }
            disposed = DisposedState;
            CryptographicOperations.ZeroMemory(key);
        }
    }
}
