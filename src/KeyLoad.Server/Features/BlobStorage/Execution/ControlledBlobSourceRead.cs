using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.BlobStorage;

internal sealed class ControlledBlobSourceRead(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> nodeOptions, IOptions<DatabaseLimits> limits,
    RemoteDocumentClient client, TimeProvider clock)
{
    private const int RevalidationPasses = 2;
    private const long MinimumBytes = 0;
    private const string NonceFormat = "N";

    internal async Task<(bool Routed, object? Value)> TryReadAsync(GrainRequestEnvelope envelope,
        string principalId, ControlledBlobReadPurpose purpose, ReadOnlyMemory<byte> nativeRequest, CancellationToken token)
    {
        var work = new ReadExecutionBudget(limits, clock, token);
        var expiry = clock.GetUtcNow() + TimeSpan.FromSeconds(limits.Value.QueryDeadlineSeconds);
        if (expiry > envelope.ExpiresAt)
        { expiry = envelope.ExpiresAt; }
        var frame = partition.Database.TryCaptureControlledBlobRead(principalId, purpose, nativeRequest, expiry, work);
        if (frame is null)
        { return (false, null); }
        var observed = await ReadFrameAsync(envelope.RequestId, frame, work, token).ConfigureAwait(false);
        await partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        partition.Database.ValidateControlledBlobRead(frame, work);
        work.Check();
        return (true, observed.NativeValue);
    }

    internal async Task<OperationResult> ObserveOutcomeAsync(Guid ingressRequestId,
        ControlledBlobReadFrame frame, ReadExecutionBudget work, CancellationToken token)
    {
        var observed = await ReadFrameAsync(ingressRequestId, frame, work, token).ConfigureAwait(false);
        if (!observed.OutcomeValidated || observed.NativeValue is not null || frame.OriginalOutcome is null)
        { throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof); }
        await partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        partition.Database.ValidateControlledBlobOutcome(frame, work);
        work.Check();
        return frame.OriginalOutcome.Result;
    }

    private async Task<ControlledBlobReadResult> ReadFrameAsync(Guid ingressRequestId,
        ControlledBlobReadFrame frame, ReadExecutionBudget work, CancellationToken token)
    {
        var destination = RequireDestination(frame);
        var metadata = work.ReadBytes;
        var maximumBytes = Math.Min(limits.Value.MaxBatchBytes,
            limits.Value.MaxQueryReadBytes - checked(metadata * RevalidationPasses));
        if (maximumBytes <= MinimumBytes)
        { throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteDocumentProtocol.Unavailable); }
        var maximumRecords = Math.Min(limits.Value.MaxBatchMutations, limits.Value.MaxScanRecords);
        var grant = work.CreateReadGrant(maximumBytes, maximumRecords);
        var maximumReply = Math.Min(limits.Value.MaxBatchBytes, RemoteDocumentProtocol.MaximumBodyBytes);
        var capability = new ControlledBlobReadRequest(frame, maximumBytes, maximumRecords, maximumReply);
        var discovery = node.Discovery?.Read() ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);
        var call = new RemoteControlledBlobCall(RemoteDocumentProtocol.Version, ingressRequestId,
            Guid.NewGuid().ToString(NonceFormat), partition.Configuration.LocalId, discovery.SiloAddress,
            PhysicalOwnerConfiguredTuples.Local(nodeOptions.Value, partition).Owner, destination, capability, maximumReply);
        var observed = await client.ReadControlledBlobAsync(call, token).ConfigureAwait(false);
        work.ImportReadGrant(grant, observed.ReadBytes, observed.ExaminedRecords);
        work.MeasureResult(observed);
        work.Check();
        return observed;
    }

    private RegisteredPhysicalOwnerV1 RequireDestination(ControlledBlobReadFrame frame)
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
