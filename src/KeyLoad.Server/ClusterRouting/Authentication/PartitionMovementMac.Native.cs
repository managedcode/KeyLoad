using System.Security.Cryptography;
using System.Text;
using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ClusterRouting;

internal sealed partial class PartitionMovementMac
{
    private static readonly byte[] NativeRequestDomain = Encoding.ASCII.GetBytes(PartitionMovementGrainServiceProtocol.NativeRequestPurpose);
    private static readonly byte[] NativeReplyDomain = Encoding.ASCII.GetBytes(PartitionMovementGrainServiceProtocol.NativeReplyPurpose);

    internal string SignNativeRequest(PartitionMovementGrainServiceRequest request)
        => Convert.ToHexStringLower(DigestNative(NativeSerialization.Serialize(request with
        {
            SenderSignature = string.Empty
        }), NativeRequestDomain));

    internal bool VerifyNativeRequest(PartitionMovementGrainServiceRequest request)
        => VerifyNative(NativeSerialization.Serialize(request with
        {
            SenderSignature = string.Empty
        }), request.SenderSignature, NativeRequestDomain);

    internal string SignNativeReply(PartitionMovementGrainServiceReply reply)
        => Convert.ToHexStringLower(DigestNative(NativeSerialization.Serialize(reply with
        {
            SenderSignature = string.Empty
        }), NativeReplyDomain));

    internal bool VerifyNativeReply(PartitionMovementGrainServiceReply reply)
        => VerifyNative(NativeSerialization.Serialize(reply with
        {
            SenderSignature = string.Empty
        }), reply.SenderSignature, NativeReplyDomain);

    internal bool VerifyNative(ReadOnlySpan<byte> body, string signature, ReadOnlySpan<byte> domain)
    {
        if (signature is null || signature.Length != SignatureCharacters
            || signature.Any(character => !char.IsAsciiHexDigitLower(character)))
        {
            return false;
        }
        return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(signature), DigestNative(body, domain));
    }

    internal byte[] DigestNative(ReadOnlySpan<byte> body, ReadOnlySpan<byte> domain)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref disposed) != Active, this);
        var maximum = checked(maximumBytes + ReplicaTransportProtocol.MaximumEnvelopeOverheadBytes);
        if (body.IsEmpty || body.Length > maximum)
        {
            throw Errors.Fail(ErrorCode.ResourceExhausted, InvalidProof);
        }
        using var hash = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, key);
        hash.AppendData(domain);
        hash.AppendData(body);
        return hash.GetHashAndReset();
    }
}
