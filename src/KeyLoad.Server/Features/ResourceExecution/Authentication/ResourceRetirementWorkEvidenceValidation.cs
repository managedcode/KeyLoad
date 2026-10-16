using System.Collections.Immutable;
using KeyLoad.Clustering;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.ResourceExecution;

/// <summary>Authenticates original native exchanges; durable history additionally needs a fresh all-voter stored-record read.</summary>
internal static class ResourceRetirementWorkEvidenceValidation
{
    internal static void Require(ImmutableArray<ResourceRetirementWorkJoinEvidenceV1> evidence,
        ResourceRetirementControlV1 control, RegisteredPhysicalOwnerV1 owner, ResourceOwnerInventoryNativeTrust trust,
        IOptions<DatabaseLimits> limits, IOptions<GrainRoutingOptions> routing, TimeProvider clock)
        => RequireSet(evidence, control, owner, null, false, trust, limits, routing, clock);

    internal static void RequireDurable(ResourceRetirementWorkOwnerReceiptV1 owner,
        ImmutableArray<ResourceRetirementWorkJoinEvidenceV1> freshReads, ResourceOwnerInventoryNativeTrust trust,
        IOptions<DatabaseLimits> limits, IOptions<GrainRoutingOptions> routing, TimeProvider clock)
    {
        ResourceRetirementWorkValidation.Owner(owner, owner.Control, limits.Value);
        RequireSet(freshReads, owner.Control, owner.Receiver, owner, false, trust, limits, routing, clock);
        RequireSet(owner.Evidence, owner.Control, owner.Receiver, null, true, trust, limits, routing, clock);
    }

    private static void RequireSet(ImmutableArray<ResourceRetirementWorkJoinEvidenceV1> evidence,
        ResourceRetirementControlV1 control, RegisteredPhysicalOwnerV1 owner, ResourceRetirementWorkOwnerReceiptV1? durable,
        bool historical, ResourceOwnerInventoryNativeTrust trust, IOptions<DatabaseLimits> limits,
        IOptions<GrainRoutingOptions> routing, TimeProvider clock)
    {
        if (evidence.IsDefaultOrEmpty || evidence.Length != owner.Owner.VoterIds.Length)
        {
            throw Invalid();
        }
        using var mac = trust.CreateMac();
        var voters = new HashSet<string>(StringComparer.Ordinal);
        var addresses = new HashSet<string>(StringComparer.Ordinal);
        var nodes = new HashSet<Guid>();
        foreach (var item in evidence)
        {
            ResourceRetirementWorkValidation.Evidence(item, item.Witness.Fence, owner.Owner, limits.Value);
            RequireOne(item, control, owner, durable, historical, mac, limits, routing, clock);
            if (!voters.Add(item.Witness.VoterId) || !addresses.Add(item.Witness.FullSiloAddress)
                || !nodes.Add(item.Witness.StoreNodeId))
            {
                throw Invalid();
            }
        }
        if (!voters.SetEquals(owner.Owner.VoterIds))
        {
            throw Invalid();
        }
    }

    private static void RequireOne(ResourceRetirementWorkJoinEvidenceV1 item, ResourceRetirementControlV1 control,
        RegisteredPhysicalOwnerV1 owner, ResourceRetirementWorkOwnerReceiptV1? durable, bool historical,
        PartitionMovementMac mac, IOptions<DatabaseLimits> limits, IOptions<GrainRoutingOptions> routing, TimeProvider clock)
    {
        if (item.SignedNativeRequest.Length > limits.Value.MaxBatchBytes
            || item.SignedNativeReply.Length > limits.Value.MaxBatchBytes)
        {
            throw Invalid();
        }
        var request = NativeSerialization.Deserialize<ResourceRetirementNativeRequest>(item.SignedNativeRequest.Span);
        var reply = NativeSerialization.Deserialize<ResourceRetirementNativeReply>(item.SignedNativeReply.Span);
        if (!NativeSerialization.Serialize(request).AsSpan().SequenceEqual(item.SignedNativeRequest.Span)
            || !NativeSerialization.Serialize(reply).AsSpan().SequenceEqual(item.SignedNativeReply.Span))
        {
            throw Invalid();
        }
        RequireSignatures(request, reply, historical, mac, limits, routing, clock);
        if (!request.Control.Owner.VoterIds.Contains(request.CallerVoter, StringComparer.Ordinal)
            || !PhysicalOwnerEntryValidation.Same(request.Destination, owner)
            || !PhysicalOwnerEntryValidation.SameOwner(request.Control.Owner, control.Catalog.DefaultShard)
            || request.Command || request.CommandId != Guid.Empty)
        {
            throw Invalid();
        }
        var query = NativeSerialization.Deserialize<ResourceRetirementReadRequestV1>(request.Payload.Span);
        ResourceRetirementNativeReadValidation.RequireRequest(request, query, limits.Value);
        var result = NativeSerialization.Deserialize<ResourceRetirementReadReplyV1>(reply.Payload.Span);
        ResourceRetirementNativeReadValidation.RequireReply(query, result, limits.Value);
        if (reply.Error is not null || query.Operation != ResourceRetirementReadOperation.ActualOwnerWorkJoin
            || query.AcknowledgedWork is not null || !ResourceRetirementValidation.Same(query.Control, control)
            || !ResourceRetirementValidation.Same(result.WorkJoinWitness, item.Witness)
            || !ResourceRetirementValidation.Same(result.AcknowledgedOwner, durable)
            || item.Witness.VoterId != request.DestinationVoter
            || durable is null && item.Witness.FullSiloAddress != request.DestinationSiloAddress)
        {
            throw Invalid();
        }
    }

    private static void RequireSignatures(ResourceRetirementNativeRequest request, ResourceRetirementNativeReply reply,
        bool historical, PartitionMovementMac mac, IOptions<DatabaseLimits> limits,
        IOptions<GrainRoutingOptions> routing, TimeProvider clock)
    {
        if (historical)
        {
            ResourceRetirementEnvelopeValidation.RequireHistoricalRequest(mac, request, limits, routing, clock);
            ResourceRetirementEnvelopeValidation.RequireHistoricalReply(mac, request, reply, limits, clock);
        }
        else
        {
            ResourceRetirementEnvelopeValidation.RequireRequest(mac, request, limits, routing, clock);
            ResourceRetirementEnvelopeValidation.RequireReply(mac, request, reply, limits, clock);
        }
        if (reply.Error is not null)
        {
            throw Invalid();
        }
    }

    private static KeyLoadException Invalid()
        => Errors.Fail(ErrorCode.Unauthenticated, ResourceRetirementNativeProtocol.InvalidProof);
}
