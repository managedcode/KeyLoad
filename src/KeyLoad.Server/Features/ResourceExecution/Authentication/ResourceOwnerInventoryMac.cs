using System.Text;
using KeyLoad.Clustering;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Borrows the existing native peer MAC owner under inventory-only request and reply domains.</summary>
internal static class ResourceOwnerInventoryMac
{
    private const char SignaturePlaceholderCharacter = '0';
    private static readonly byte[] RequestDomain = Encoding.ASCII.GetBytes(ResourceOwnerInventoryNativeProtocol.RequestPurpose);
    private static readonly byte[] ReplyDomain = Encoding.ASCII.GetBytes(ResourceOwnerInventoryNativeProtocol.ReplyPurpose);
    private static readonly string SignatureWidth = new(SignaturePlaceholderCharacter, ReplicaTransportProtocol.HashHexCharacters);

    internal static string SignRequest(PartitionMovementMac mac, ResourceOwnerInventoryNativeRequest request,
        IOptions<DatabaseLimits> limits)
    {
        ResourceOwnerInventoryEnvelopeValidation.RequireEnvelopeSize(request with
        {
            Signature = SignatureWidth
        }, limits);
        return Convert.ToHexStringLower(mac.DigestNative(NativeSerialization.Serialize(request with
        {
            Signature = string.Empty
        }), RequestDomain));
    }

    internal static bool VerifyRequest(PartitionMovementMac mac, ResourceOwnerInventoryNativeRequest request,
        IOptions<DatabaseLimits> limits)
    {
        if (!ResourceOwnerInventoryEnvelopeValidation.IsSignature(request.Signature))
        {
            return false;
        }
        ResourceOwnerInventoryEnvelopeValidation.RequireEnvelopeSize(request, limits);
        return mac.VerifyNative(NativeSerialization.Serialize(request with
        {
            Signature = string.Empty
        }), request.Signature, RequestDomain);
    }

    internal static string SignReply(PartitionMovementMac mac, ResourceOwnerInventoryNativeReply reply,
        IOptions<DatabaseLimits> limits)
    {
        ResourceOwnerInventoryEnvelopeValidation.RequireEnvelopeSize(reply with
        {
            Signature = SignatureWidth
        }, limits);
        return Convert.ToHexStringLower(mac.DigestNative(NativeSerialization.Serialize(reply with
        {
            Signature = string.Empty
        }), ReplyDomain));
    }

    internal static bool VerifyReply(PartitionMovementMac mac, ResourceOwnerInventoryNativeReply reply,
        IOptions<DatabaseLimits> limits)
    {
        if (!ResourceOwnerInventoryEnvelopeValidation.IsSignature(reply.Signature))
        {
            return false;
        }
        ResourceOwnerInventoryEnvelopeValidation.RequireEnvelopeSize(reply, limits);
        return mac.VerifyNative(NativeSerialization.Serialize(reply with
        {
            Signature = string.Empty
        }), reply.Signature, ReplyDomain);
    }
}
