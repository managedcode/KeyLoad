using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Server.Features.ClusterRouting;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.DocumentStorage;

internal sealed class ControlledDocumentSourceRead(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> nodeOptions, IOptions<DatabaseLimits> limits,
    RemoteDocumentClient client, TimeProvider clock)
{
    private const int RevalidationPasses = 2;
    private const long MinimumBytes = 0;
    private const string NonceFormat = "N";

    internal async Task<(bool Routed, DocumentResult? Document)> TryReadAsync(GrainRequestEnvelope envelope,
        string principalId, GetDocumentRequest request, ReadExecutionBudget work, CancellationToken token, IResourceLifetimeWorkBinding binding)
    {
        var expiry = clock.GetUtcNow() + TimeSpan.FromSeconds(limits.Value.QueryDeadlineSeconds);
        if (expiry > envelope.ExpiresAt)
        {
            expiry = envelope.ExpiresAt;
        }
        var frame = partition.Database.TryCaptureControlledDocumentReadV2Bound(principalId,
            request.Reference, request.MinimumToken, expiry, work, binding);
        if (frame is null)
        {
            return (false, null);
        }
        var destination = RequireDestination(frame.Original);
        var metadata = work.ReadBytes;
        var maximumBytes = Math.Min(limits.Value.MaxBatchBytes,
            limits.Value.MaxQueryReadBytes - checked(metadata * RevalidationPasses));
        if (maximumBytes <= MinimumBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteDocumentProtocol.Unavailable);
        }
        var maximumRecords = Math.Min(limits.Value.MaxBatchMutations, limits.Value.MaxScanRecords);
        var grant = work.CreateReadGrant(maximumBytes, maximumRecords);
        var maximumReply = Math.Min(limits.Value.MaxBatchBytes, RemoteDocumentProtocol.MaximumBodyBytes);
        var capability = new ControlledDocumentReadRequestV2(frame, maximumBytes, maximumRecords, maximumReply);
        var discovery = node.Discovery?.Read() ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);
        var call = new RemoteResourceLifetimeReadCallV2(RemoteResourceLifetimeReadProtocol.Version,
            RemoteResourceLifetimeReadKindV2.ControlledDocument, envelope.RequestId, Guid.NewGuid().ToString(NonceFormat),
            frame.Original.ExpiresAt, partition.Configuration.LocalId, discovery.SiloAddress,
            PhysicalOwnerConfiguredTuples.Local(nodeOptions.Value, partition).Owner, destination,
            NativeSerialization.Serialize(capability), maximumReply);
        var bytes = await client.ReadLifetimeAsync(call, token).ConfigureAwait(false);
        var observed = NativeSerialization.Deserialize<ControlledDocumentReadResultV2>(bytes.Span);
        work.ImportReadGrant(grant, observed.ReadBytes, observed.ExaminedRecords);
        work.MeasureResult(observed);
        await partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        partition.Database.ValidateControlledDocumentReadV2Bound(frame, work, binding);
        work.Check();
        return (true, observed.Document);
    }

    private RegisteredPhysicalOwnerV1 RequireDestination(PartitionControlDocumentReadFrame frame)
    {
        var settings = nodeOptions.Value.PhysicalGroupTrust;
        var destination = PhysicalOwnerConfiguredTuples.Destination(nodeOptions.Value, partition);
        if (!settings.RemoteDocumentReads || !settings.RegisterPhysicalOwners
            || settings.Role != PhysicalGroupTrustRole.Control
            || !PhysicalOwnerEntryValidation.SameOwner(frame.Publication.Destination, destination.Owner))
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);
        }
        return destination;
    }
}
