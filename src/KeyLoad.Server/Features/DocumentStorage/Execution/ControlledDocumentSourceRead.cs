using KeyLoad.Core;
using KeyLoad.Core.Features.ClusterRouting.Contracts;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
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
        string principalId, GetDocumentRequest request, CancellationToken token)
    {
        var work = new ReadExecutionBudget(limits, clock, token);
        var expiry = clock.GetUtcNow() + TimeSpan.FromSeconds(limits.Value.QueryDeadlineSeconds);
        if (expiry > envelope.ExpiresAt)
        { expiry = envelope.ExpiresAt; }
        var frame = partition.Database.TryCaptureControlledDocumentRead(principalId,
            request.Reference, request.MinimumToken, expiry, work);
        if (frame is null)
        { return (false, null); }
        var destination = RequireDestination(frame);
        var metadata = work.ReadBytes;
        var maximumBytes = Math.Min(limits.Value.MaxBatchBytes,
            limits.Value.MaxQueryReadBytes - checked(metadata * RevalidationPasses));
        if (maximumBytes <= MinimumBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteDocumentProtocol.Unavailable); }
        var maximumRecords = Math.Min(limits.Value.MaxBatchMutations, limits.Value.MaxScanRecords);
        var grant = work.CreateReadGrant(maximumBytes, maximumRecords);
        var maximumReply = Math.Min(limits.Value.MaxBatchBytes, RemoteDocumentProtocol.MaximumBodyBytes);
        var capability = new ControlledDocumentReadRequest(frame, maximumBytes, maximumRecords, maximumReply);
        var discovery = node.Discovery?.Read() ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);
        var call = new RemoteControlledDocumentCall(RemoteDocumentProtocol.Version, envelope.RequestId,
            Guid.NewGuid().ToString(NonceFormat), partition.Configuration.LocalId, discovery.SiloAddress,
            PhysicalOwnerConfiguredTuples.Local(nodeOptions.Value, partition).Owner, destination, capability, maximumReply);
        var observed = await client.ReadControlledAsync(call, token).ConfigureAwait(false);
        work.ImportReadGrant(grant, observed.ReadBytes, observed.ExaminedRecords);
        work.MeasureResult(observed);
        await partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        partition.Database.ValidateControlledDocumentRead(frame, work);
        work.Check();
        return (true, observed.Document);
    }

    private RegisteredPhysicalOwnerV1 RequireDestination(PartitionControlDocumentReadFrame frame)
    {
        var settings = nodeOptions.Value.MembershipAuthority;
        var destination = PhysicalOwnerConfiguredTuples.Destination(nodeOptions.Value, partition);
        if (!settings.RemoteDocumentReads || !settings.RegisterPhysicalOwners
            || settings.Mode != MembershipAuthoritySettingsProtocol.Authority
            || !PhysicalOwnerEntryValidation.SameOwner(frame.Publication.Destination, destination.Owner))
        { throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable); }
        return destination;
    }
}
