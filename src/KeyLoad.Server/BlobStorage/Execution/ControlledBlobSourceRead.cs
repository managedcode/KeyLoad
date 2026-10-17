using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.ResourceExecution;
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
        string principalId, ControlledBlobReadPurpose purpose, ReadOnlyMemory<byte> nativeRequest,
        ReadExecutionBudget work, IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        var expiry = clock.GetUtcNow() + TimeSpan.FromSeconds(limits.Value.QueryDeadlineSeconds);
        if (expiry > envelope.ExpiresAt)
        {
            expiry = envelope.ExpiresAt;
        }
        var frame = partition.Database.TryCaptureControlledBlobReadV2Bound(principalId, purpose, nativeRequest, expiry, work, binding);
        if (frame is null)
        {
            return (false, null);
        }
        var observed = await ReadDataFrameAsync(envelope.RequestId, frame, work, token).ConfigureAwait(false);
        await partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        partition.Database.ValidateControlledBlobReadV2(frame, work);
        work.Check();
        return (true, observed.NativeValue);
    }

    internal async Task<OperationResult> ObserveOutcomeAsync(Guid ingressRequestId,
        ControlledBlobReadFrameV2 frame, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding, CancellationToken token)
    {
        ArgumentNullException.ThrowIfNull(binding);
        work.AttachResourceLifetimeBinding(binding);
        var observed = await ReadOutcomeFrameAsync(ingressRequestId, frame, work, token).ConfigureAwait(false);
        if (!observed.OutcomeValidated || observed.NativeValue is not null || frame.Original.OriginalOutcome is null)
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, RemoteDocumentProtocol.InvalidProof);
        }
        await partition.Coordinator.ReadBarrierAsync(token).ConfigureAwait(false);
        partition.Database.ValidateControlledBlobOutcomeV2(frame, work);
        work.Check();
        return frame.Original.OriginalOutcome.Result;
    }

    private async Task<ControlledBlobReadResult> ReadOutcomeFrameAsync(Guid ingressRequestId,
        ControlledBlobReadFrameV2 frame, ReadExecutionBudget work, CancellationToken token)
    {
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
        var capability = new ControlledBlobReadRequest(frame.Original, maximumBytes, maximumRecords, maximumReply);
        var discovery = node.Discovery?.Read() ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);
        var call = new RemoteControlledBlobCall(RemoteDocumentProtocol.Version, ingressRequestId,
            Guid.NewGuid().ToString(NonceFormat), partition.Configuration.LocalId, discovery.SiloAddress,
            PhysicalOwnerConfiguredTuples.Local(nodeOptions.Value, partition).Owner, destination, capability, maximumReply);
        var wrapped = new RemoteControlledBlobOutcomeCallV2(call, frame.Coverage);
        if (NativeSerialization.Measure(wrapped) > limits.Value.MaxBatchBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteDocumentProtocol.Unavailable);
        }
        work.MeasureResult(wrapped);
        var observed = await client.ReadControlledBlobOutcomeV2Async(wrapped, token).ConfigureAwait(false);
        work.ImportReadGrant(grant, observed.ReadBytes, observed.ExaminedRecords);
        work.MeasureResult(observed);
        work.Check();
        return observed;
    }

    private async Task<ControlledBlobReadResultV2> ReadDataFrameAsync(Guid ingressRequestId,
        ControlledBlobReadFrameV2 frame, ReadExecutionBudget work, CancellationToken token)
    {
        var original = frame.Original;
        var destination = RequireDestination(original);
        var maximumBytes = Math.Min(limits.Value.MaxBatchBytes,
            limits.Value.MaxQueryReadBytes - checked(work.ReadBytes * RevalidationPasses));
        if (maximumBytes <= MinimumBytes)
        {
            throw Errors.Fail(ErrorCode.BudgetExceeded, RemoteDocumentProtocol.Unavailable);
        }
        var maximumRecords = Math.Min(limits.Value.MaxBatchMutations, limits.Value.MaxScanRecords);
        var grant = work.CreateReadGrant(maximumBytes, maximumRecords);
        var maximumReply = Math.Min(limits.Value.MaxBatchBytes, RemoteDocumentProtocol.MaximumBodyBytes);
        var capability = new ControlledBlobReadRequestV2(frame, maximumBytes, maximumRecords, maximumReply);
        var discovery = node.Discovery?.Read() ?? throw Errors.Fail(ErrorCode.OwnershipLost, RemoteDocumentProtocol.Unavailable);
        var call = new RemoteResourceLifetimeReadCallV2(RemoteResourceLifetimeReadProtocol.Version,
            RemoteResourceLifetimeReadKindV2.ControlledBlob, ingressRequestId, Guid.NewGuid().ToString(NonceFormat),
            original.ExpiresAt, partition.Configuration.LocalId, discovery.SiloAddress,
            PhysicalOwnerConfiguredTuples.Local(nodeOptions.Value, partition).Owner, destination,
            NativeSerialization.Serialize(capability), maximumReply);
        var bytes = await client.ReadLifetimeAsync(call, token).ConfigureAwait(false);
        var observed = NativeSerialization.Deserialize<ControlledBlobReadResultV2>(bytes.Span);
        if (observed is null || !ValidDataResult(original.Purpose, observed.NativeValue))
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteDocumentProtocol.InvalidProof);
        }
        work.ImportReadGrant(grant, observed.ReadBytes, observed.ExaminedRecords);
        work.MeasureResult(observed);
        work.Check();
        return observed;
    }

    private static bool ValidDataResult(ControlledBlobReadPurpose purpose, object? value)
        => purpose switch
        {
            ControlledBlobReadPurpose.Metadata => value is null or BlobMetadata,
            ControlledBlobReadPurpose.UploadInfo => value is null or BlobUploadInfo,
            ControlledBlobReadPurpose.Range => value is BlobReadResult,
            ControlledBlobReadPurpose.List => value is BlobListPage,
            _ => false
        };

    private RegisteredPhysicalOwnerV1 RequireDestination(ControlledBlobReadFrame frame)
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
