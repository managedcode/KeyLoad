using System.Security.Cryptography;
using KeyLoad.Clustering;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed class ResourceOwnerInventorySignedReads(OrleansNode node, PartitionHost partition,
    PhysicalOwnerStartupRequests authentication, TimeProvider clock)
{
    internal async Task<ResourceOwnerInventoryReadReplyV1> ReadAsync(ResourceOwnerInventoryReadRequestV1 query,
        string sourcePrincipalId, long sourcePolicyEpoch, bool technicalPrincipal, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        var principal = technicalPrincipal ? await authentication.AuthenticateAsync(token).ConfigureAwait(false)
            : partition.Database.Store.Read(view => partition.Database.Principal(view, sourcePrincipalId, clock.GetUtcNow()));
        GrainRequestAuthority.RequireAdministrator(principal);
        if (!technicalPrincipal && principal.PolicyEpoch != sourcePolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
        partition.Database.VerifyPhysicalCommandOwner(query.ObservedOwner.Owner, false, token);
        query = query with
        {
            OriginalPolicyEpoch = principal.PolicyEpoch
        };
        var payload = NativeSerialization.Serialize(query);
        var requestId = Guid.NewGuid();
        string signed;
        try
        {
            signed = node.CatalogRequestCodec().CreateResourceOwnerInventoryRead(requestId, principal.Id,
                payload, query.Request.ExpiresAt);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(payload);
        }
        var failures = new List<Exception>();
        GrainOperationReply? reply = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var identity = node.OpenRequestContext(principal, requestId, Guid.Empty, token);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                reply = await node.ExecuteAsync(requestId, signed, command: false, cancellationToken: token)
                    .ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        token.ThrowIfCancellationRequested();
        if (reply?.Error is { } error)
        {
            throw Errors.Fail(error, ResourceOwnerInventoryNativeProtocol.Unavailable);
        }
        var value = (reply is not null ? GrainNativePayload.Read<GrainValue>(reply.Payload).Value
            as ResourceOwnerInventoryReadReplyV1 : null)
            ?? throw Errors.Fail(ErrorCode.Corruption, ResourceOwnerInventoryNativeProtocol.InvalidProof);
        ResourceOwnerInventoryNativeQuery.RequireReply(query, value, sourcePrincipalId, sourcePolicyEpoch);
        return value;
    }
}
