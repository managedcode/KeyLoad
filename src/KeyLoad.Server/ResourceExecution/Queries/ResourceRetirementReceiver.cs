using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

internal sealed class ResourceRetirementReceiver(ResourceOwnerInventoryNativeAuthority authority,
    ResourceOwnerInventoryNativeTrust trust, ResourceRetirementSignedExecution execution,
    IOptions<DatabaseLimits> limits, IOptions<GrainRoutingOptions> routing, TimeProvider clock)
{
    internal async Task<ResourceRetirementNativeReply> ExecuteAsync(ResourceRetirementNativeRequest request, CancellationToken token)
    {
        using var mac = trust.CreateMac();
        ResourceRetirementEnvelopeValidation.RequireRequest(mac, request, limits, routing, clock);
        var work = ResourceRetirementFrameReadBudget.Create(request.MaximumReadBytes, request.MaximumExaminedRecords,
            request.MaximumResultBytes, request.ExpiresAt, limits, clock, token);
        try
        {
            await authority.RequireReceivingAsync(request, work, null, token).ConfigureAwait(false);
            var technical = !PhysicalOwnerEntryValidation.Same(request.Control, request.Destination);
            var payload = await ExecutePayloadAsync(request, technical, work, token).ConfigureAwait(false);
            await authority.RequireReceivingAsync(request, work, null, token).ConfigureAwait(false);
            work.Check();
            token.ThrowIfCancellationRequested();
            if (request.ExpiresAt <= clock.GetUtcNow())
            {
                throw Errors.Fail(ErrorCode.CursorExpired, ResourceRetirementNativeProtocol.Unavailable);
            }
            if (payload.Length > request.MaximumResultBytes)
            {
                throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementNativeProtocol.Unavailable);
            }
            var reply = new ResourceRetirementNativeReply(request.Version, request.RequestId, request.Nonce,
                request.Signature, payload, null, null, string.Empty)
            {
                Metadata = new(work.ReadBytes, work.ConstrainedNativeRecords, payload.Length)
            };
            return reply with
            {
                Signature = ResourceRetirementMac.SignReply(mac, reply, limits)
            };
        }
        catch (KeyLoadException error)
        {
            var reply = new ResourceRetirementNativeReply(request.Version, request.RequestId, request.Nonce,
                request.Signature, ReadOnlyMemory<byte>.Empty, error.Code, ResourceRetirementNativeProtocol.Unavailable, string.Empty)
            {
                Metadata = work.HasIndeterminateMetadata ? null
                : new(work.ReadBytes, work.ConstrainedNativeRecords, ReadOnlyMemory<byte>.Empty.Length)
            };
            return reply with
            {
                Signature = ResourceRetirementMac.SignReply(mac, reply, limits)
            };
        }
    }

    private async Task<byte[]> ExecutePayloadAsync(ResourceRetirementNativeRequest outer, bool technical,
        ReadExecutionBudget work, CancellationToken token)
    {
        if (outer.Command)
        {
            var request = NativeSerialization.Deserialize<ResourceRetirementNativePhaseRequestV1>(outer.Payload.Span);
            ResourceRetirementNativePhaseValidation.RequireRequest(outer, request, limits.Value);
            var value = await execution.PhaseAsync(outer.CommandId, request, outer.PrincipalId, outer.PolicyEpoch, technical, work, token)
                .ConfigureAwait(false);
            RequireResultSize(value, outer.MaximumResultBytes);
            return NativeSerialization.Serialize(value);
        }
        var query = NativeSerialization.Deserialize<ResourceRetirementReadRequestV1>(outer.Payload.Span);
        ResourceRetirementNativeReadValidation.RequireRequest(outer, query, limits.Value);
        var result = await execution.ReadAsync(query, outer.PrincipalId, outer.PolicyEpoch, technical, work, token).ConfigureAwait(false);
        RequireResultSize(result, outer.MaximumResultBytes);
        return NativeSerialization.Serialize(result);
    }

    private static void RequireResultSize<T>(T value, int maximumBytes)
    {
        if (NativeSerialization.Measure(value) > maximumBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, ResourceRetirementNativeProtocol.Unavailable);
        }
    }
}
