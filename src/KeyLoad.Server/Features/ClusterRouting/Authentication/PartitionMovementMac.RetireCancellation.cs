using System.Security.Cryptography;
using System.Text;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementMac
{
    private const string RetireCancellationRequestPurpose = "keyload-partition-movement-retire-cancellation-request-v1";
    private const string RetireCancellationQueryPurpose = "keyload-partition-movement-retire-cancellation-query-v1";
    private const string RetireCancellationReplyPurpose = "keyload-partition-movement-retire-cancellation-reply-v1";
    private static readonly byte[] RetireCancellationRequestDomain = Encoding.ASCII.GetBytes(RetireCancellationRequestPurpose);
    private static readonly byte[] RetireCancellationQueryDomain = Encoding.ASCII.GetBytes(RetireCancellationQueryPurpose);
    private static readonly byte[] RetireCancellationReplyDomain = Encoding.ASCII.GetBytes(RetireCancellationReplyPurpose);

    internal string SignRetireCancellationRequest(ReadOnlySpan<byte> body, bool query)
        => Convert.ToHexStringLower(DigestDomain(body, query ? RetireCancellationQueryDomain : RetireCancellationRequestDomain));

    internal bool VerifyRetireCancellationRequest(ReadOnlySpan<byte> body, string signature, bool query)
    {
        if (signature.Length != SignatureCharacters || signature.Any(character => !char.IsAsciiHexDigitLower(character)))
        { return false; }
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signature),
            DigestDomain(body, query ? RetireCancellationQueryDomain : RetireCancellationRequestDomain));
    }

    internal string SignRetireCancellationReply(ReadOnlySpan<byte> body)
        => Convert.ToHexStringLower(DigestDomain(body, RetireCancellationReplyDomain));

    internal bool VerifyRetireCancellationReply(ReadOnlySpan<byte> body, string signature)
    {
        if (signature.Length != SignatureCharacters || signature.Any(character => !char.IsAsciiHexDigitLower(character)))
        { return false; }
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signature), DigestDomain(body, RetireCancellationReplyDomain));
    }
}
