using System.Text;
using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Borrows the existing native peer MAC owner under retirement-only request and reply domains.</summary>
internal static class ResourceRetirementMac
{
    private const char SignaturePlaceholderCharacter = '0';
    private static readonly byte[] RequestDomain = Encoding.ASCII.GetBytes(ResourceRetirementNativeProtocol.RequestPurpose);
    private static readonly byte[] ReplyDomain = Encoding.ASCII.GetBytes(ResourceRetirementNativeProtocol.ReplyPurpose);
    private static readonly string SignatureWidth = new(SignaturePlaceholderCharacter, ReplicaTransportProtocol.HashHexCharacters);

    internal static string SignRequest(PartitionMovementMac mac, ResourceRetirementNativeRequest request,
        IOptions<DatabaseLimits> limits)
    {
        ResourceRetirementEnvelopeValidation.RequireEnvelopeSize(request with
        {
            Signature = SignatureWidth
        }, limits);
        return Convert.ToHexStringLower(mac.DigestNative(NativeSerialization.Serialize(request with
        {
            Signature = string.Empty
        }), RequestDomain));
    }

    internal static bool VerifyRequest(PartitionMovementMac mac, ResourceRetirementNativeRequest request,
        IOptions<DatabaseLimits> limits)
    {
        if (request is null || !ResourceRetirementEnvelopeValidation.IsSignature(request.Signature))
        {
            return false;
        }
        ResourceRetirementEnvelopeValidation.RequireEnvelopeSize(request, limits);
        return mac.VerifyNative(NativeSerialization.Serialize(request with
        {
            Signature = string.Empty
        }), request.Signature, RequestDomain);
    }

    internal static string SignReply(PartitionMovementMac mac, ResourceRetirementNativeReply reply,
        IOptions<DatabaseLimits> limits)
    {
        ResourceRetirementEnvelopeValidation.RequireEnvelopeSize(reply with
        {
            Signature = SignatureWidth
        }, limits);
        return Convert.ToHexStringLower(mac.DigestNative(NativeSerialization.Serialize(reply with
        {
            Signature = string.Empty
        }), ReplyDomain));
    }

    internal static bool VerifyReply(PartitionMovementMac mac, ResourceRetirementNativeReply reply,
        IOptions<DatabaseLimits> limits)
    {
        if (reply is null || !ResourceRetirementEnvelopeValidation.IsSignature(reply.Signature))
        {
            return false;
        }
        ResourceRetirementEnvelopeValidation.RequireEnvelopeSize(reply, limits);
        return mac.VerifyNative(NativeSerialization.Serialize(reply with
        {
            Signature = string.Empty
        }), reply.Signature, ReplyDomain);
    }
}
