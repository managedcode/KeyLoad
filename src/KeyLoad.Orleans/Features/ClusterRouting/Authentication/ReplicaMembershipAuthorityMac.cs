using Microsoft.Extensions.Options;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Orleans;

internal sealed class ReplicaMembershipAuthorityMac(ReadOnlyMemory<byte> credential, IOptions<OrleansMembershipOptions> options) : IDisposable
{
    private const string SignRequestFieldsText = "POST";

    private static readonly UTF8Encoding Utf8 = new(false, true);
    private readonly OrleansMembershipOptions settings = options.Value;
    private readonly byte[] secret = credential.ToArray();

    internal string SignRequest(string cluster, string authorityPhysical, string authorityIncarnation,
        string callerPhysical, string callerIncarnation, string callerVoter, string callerSilo,
        string timestamp, string nonce, ReadOnlySpan<byte> body)
        => Sign(ReplicaMembershipAuthorityProtocol.RequestPurpose,
            [SignRequestFieldsText, ReplicaMembershipAuthorityProtocol.Path, cluster, authorityPhysical, authorityIncarnation,
                callerPhysical, callerIncarnation, callerVoter, callerSilo, timestamp, nonce], body);

    internal bool VerifyRequest(string cluster, string authorityPhysical, string authorityIncarnation,
        string callerPhysical, string callerIncarnation, string callerVoter, string callerSilo,
        string timestamp, string nonce, ReadOnlySpan<byte> body, string signature)
        => Verify(SignRequest(cluster, authorityPhysical, authorityIncarnation, callerPhysical, callerIncarnation,
            callerVoter, callerSilo, timestamp, nonce, body), signature);

    internal string SignReply(string authorityPhysical, string authorityIncarnation, string requestId,
        string nonce, int statusCode, ReadOnlySpan<byte> body)
        => Sign(ReplicaMembershipAuthorityProtocol.ReplyPurpose,
            [authorityPhysical, authorityIncarnation, requestId, nonce,
                statusCode.ToString(System.Globalization.CultureInfo.InvariantCulture)], body);

    internal bool VerifyReply(string authorityPhysical, string authorityIncarnation, string requestId,
        string nonce, int statusCode, ReadOnlySpan<byte> body, string signature)
        => Verify(SignReply(authorityPhysical, authorityIncarnation, requestId, nonce, statusCode, body), signature);

    private string Sign(string purpose, string[] fields, ReadOnlySpan<byte> body)
    {
        const char LineFeedCharacter = '\n';
        const string SignFailureMessage = "The membership authority authentication metadata is too large.";
        const int StartEmptyCount = 0;

        using var buffer = new MemoryStream(settings.AuthenticationScratchBytes);
        buffer.Write(Utf8.GetBytes(purpose));
        buffer.WriteByte((byte)LineFeedCharacter);
        foreach (var field in fields)
        { WriteField(buffer, field); }
        Span<byte> bodyLength = stackalloc byte[sizeof(ulong)];
        BinaryPrimitives.WriteUInt64BigEndian(bodyLength, checked((ulong)body.Length));
        buffer.Write(bodyLength);
        buffer.Write(SHA256.HashData(body));
        if (buffer.Length > settings.MaximumHeaderBytes)
        { throw new InvalidOperationException(SignFailureMessage); }
        return Convert.ToBase64String(HMACSHA256.HashData(secret, buffer.GetBuffer().AsSpan(StartEmptyCount, checked((int)buffer.Length))));
    }

    private static void WriteField(Stream destination, string field)
    {
        var bytes = Utf8.GetBytes(field);
        Span<byte> length = stackalloc byte[sizeof(uint)];
        BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)bytes.Length));
        destination.Write(length);
        destination.Write(bytes);
    }

    private static bool Verify(string expected, string signature)
    {
        const int VerifyElementCount = 32;

        Span<byte> supplied = stackalloc byte[VerifyElementCount];
        return Convert.TryFromBase64String(signature, supplied, out var written) && written == supplied.Length
            && CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(expected), supplied);
    }

    public void Dispose() => CryptographicOperations.ZeroMemory(secret);
}
