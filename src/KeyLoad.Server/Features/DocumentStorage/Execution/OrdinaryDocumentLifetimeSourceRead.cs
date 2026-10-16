using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class OrdinaryDocumentLifetimeSourceRead(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> nodeOptions, IOptions<DatabaseLimits> limits, RemoteDocumentClient client, TimeProvider clock)
{
    private const int RevalidationPasses = 2;
    private const long Empty = 0;
    private const string NonceFormat = "N";

    internal async Task<(bool Routed, DocumentResult? Document)> TryReadAsync(GrainRequestEnvelope envelope,
        string principalId, GetDocumentRequest request, CancellationToken token)
    {
        var work = new ReadExecutionBudget(limits, clock, token);
        var expiry = clock.GetUtcNow() + TimeSpan.FromSeconds(limits.Value.QueryDeadlineSeconds);
        if (expiry > envelope.ExpiresAt)
        {
            expiry = envelope.ExpiresAt;
        }
        var frame = partition.Database.CaptureRemoteDocumentReadV2(principalId, request.Reference, expiry, work);
        if (frame is null)
        {
            return (false, null);
        }
        var settings = nodeOptions.Value.PhysicalGroupTrust;
        if (!settings.RemoteDocumentReads || settings.Role != PhysicalGroupTrustRole.Control
            || !PhysicalOwnerEntryValidation.Same(frame.Route.Destination,
                PhysicalOwnerConfiguredTuples.Destination(nodeOptions.Value, partition)))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);
        }
        var maximumBytes = Math.Min(limits.Value.MaxBatchBytes,
            limits.Value.MaxQueryReadBytes - checked(work.ReadBytes * RevalidationPasses));
        if (maximumBytes <= Empty)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteDocumentProtocol.Unavailable);
        }
        var maximumRecords = Math.Min(limits.Value.MaxBatchMutations, limits.Value.MaxScanRecords);
        var grant = work.CreateReadGrant(maximumBytes, maximumRecords);
        var maximumReply = Math.Min(limits.Value.MaxBatchBytes, RemoteDocumentProtocol.MaximumBodyBytes);
        var payload = new OwnedDocumentReadRequestV2(frame, request, frame.Route.Destination.Owner,
            maximumBytes, maximumRecords, maximumReply);
        var discovery = node.Discovery?.Read() ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);
        var call = new RemoteResourceLifetimeReadCallV2(RemoteResourceLifetimeReadProtocol.Version,
            RemoteResourceLifetimeReadKindV2.Document, envelope.RequestId, Guid.NewGuid().ToString(NonceFormat), expiry,
            partition.Configuration.LocalId, discovery.SiloAddress,
            PhysicalOwnerConfiguredTuples.Local(nodeOptions.Value, partition).Owner, frame.Route.Destination,
            NativeSerialization.Serialize(payload), maximumReply);
        var bytes = await client.ReadLifetimeAsync(call, token).ConfigureAwait(false);
        var observed = NativeSerialization.Deserialize<OwnedDocumentReadResultV2>(bytes.Span);
        RequireResult(observed, frame);
        work.ImportReadGrant(grant, observed.ReadBytes, observed.ExaminedRecords);
        work.CheckResult(observed);
        await partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        partition.Database.ValidateRemoteDocumentReadV2(frame, work);
        work.Check();
        return (true, observed.Result.Document);
    }
    private static void RequireResult(OwnedDocumentReadResultV2 observed, RemoteDocumentReadFrameV2 frame)
    {
        var owner = frame.Route.Destination.Owner;
        var result = observed.Result;
        if (result is null || result.NodeId == Guid.Empty || result.Incarnation != owner.Incarnation
            || result.ReadGeneration < Empty || result.AppliedPosition < Empty || result.StorePosition < Empty
            || result.Placement is null || result.Placement.PhysicalShardId != owner.PhysicalShardId
            || result.Placement.Incarnation != owner.Incarnation || result.Placement.Partition != frame.Reference.Partition
            || result.Placement.PlacementEpoch != owner.PlacementEpoch
            || !result.Placement.VoterIds.SequenceEqual(owner.VoterIds, StringComparer.Ordinal)
            || result.Document is { } document && document.Reference != frame.Reference)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);
        }
    }
}
