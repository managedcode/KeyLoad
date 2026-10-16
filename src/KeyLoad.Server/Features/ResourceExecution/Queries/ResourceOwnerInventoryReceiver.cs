using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed class ResourceOwnerInventoryReceiver(ResourceOwnerInventoryNativeAuthority authority,
    ResourceOwnerInventoryNativeTrust trust, ResourceOwnerInventorySignedReads reads,
    IOptions<DatabaseLimits> limits, IOptions<GrainRoutingOptions> routing, TimeProvider clock)
{
    internal async Task<ResourceOwnerInventoryNativeReply> ReadAsync(ResourceOwnerInventoryNativeRequest request,
        CancellationToken token)
    {
        using var mac = trust.CreateMac();
        if (request is null || !ResourceOwnerInventoryMac.VerifyRequest(mac, request, limits))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, ResourceOwnerInventoryNativeProtocol.InvalidProof);
        }
        ResourceOwnerInventoryEnvelopeValidation.RequireRequest(request, limits, routing, clock);
        try
        {
            await authority.RequireReceivingAsync(request, token).ConfigureAwait(false);
            var query = NativeSerialization.Deserialize<ResourceOwnerInventoryReadRequestV1>(request.Payload.Span);
            ResourceOwnerInventoryNativeQuery.RequireRequest(request, query);
            var technical = !PhysicalOwnerEntryValidation.Same(request.Control, request.Destination);
            var value = await reads.ReadAsync(query, request.PrincipalId, request.PolicyEpoch, technical, token)
                .ConfigureAwait(false);
            await authority.RequireReceivingAsync(request, token).ConfigureAwait(false);
            token.ThrowIfCancellationRequested();
            if (request.ExpiresAt <= clock.GetUtcNow())
            {
                throw Errors.Fail(ErrorCode.CursorExpired, ResourceOwnerInventoryNativeProtocol.Unavailable);
            }
            var payload = NativeSerialization.Serialize(value);
            if (payload.Length > request.MaximumResultBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceOwnerInventoryNativeProtocol.Unavailable);
            }
            var reply = new ResourceOwnerInventoryNativeReply(request.Version, request.RequestId, request.Nonce,
                request.Signature, payload, null, null, string.Empty);
            return reply with
            {
                Signature = ResourceOwnerInventoryMac.SignReply(mac, reply, limits)
            };
        }
        catch (KeyLoadException error)
        {
            var reply = new ResourceOwnerInventoryNativeReply(request.Version, request.RequestId, request.Nonce,
                request.Signature, ReadOnlyMemory<byte>.Empty, error.Code,
                ResourceOwnerInventoryNativeProtocol.Unavailable, string.Empty);
            return reply with
            {
                Signature = ResourceOwnerInventoryMac.SignReply(mac, reply, limits)
            };
        }
    }
}
