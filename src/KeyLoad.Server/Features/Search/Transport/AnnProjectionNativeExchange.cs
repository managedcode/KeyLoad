using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Server.Features.ClusterRouting;
using KeyLoad.Server.Features.DocumentStorage;
using Microsoft.Extensions.Options;

namespace KeyLoad.Server.Features.Search;

internal sealed class AnnProjectionNativeExchange(OrleansNode node, PartitionHost partition,
    IOptions<NodeOptions> options, TimeProvider clock, RemoteAnnReadFence capturedSource,
    AnnMaintenanceRequest originalRequest)
{
    internal async Task<AnnProjectionNativeObservation> ReadAsync(RegisteredPhysicalOwnerV1 destination,
        int voterIndex, string principalId, AnnProjectionObservationRequest request, DateTimeOffset expiry,
        ReadExecutionBudget work, CancellationToken cancellationToken)
    {
        work.Check();
        cancellationToken.ThrowIfCancellationRequested();
        if (expiry <= clock.GetUtcNow())
        { throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProjectionObservationProtocol.Invalid); }
        var encoded = AnnProjectionNativeEncoding.EncodeCall(
            new(AnnProjectionObservationProtocol.Version, principalId, request, expiry),
            work, RemoteDocumentProtocol.MaximumBodyBytes);
        var call = encoded.Call;
        var payload = encoded.Bytes;
        var failures = new List<Exception>();
        AnnProjectionNativeObservation? observed = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var source = PhysicalOwnerConfiguredTuples.Local(options.Value, partition).Owner;
            using var mac = RemoteDocumentMac.FromConfiguredSecret(RemoteModelNativeIdentity.Secret(options.Value, partition, source));
            var maximum = Math.Min(RemoteDocumentProtocol.MaximumBodyBytes, Math.Min(work.RemainingReadGrantBytes,
                node.CatalogRequestCodec().ExecutionOptions.Value.MaximumCompletedBytes));
            var reply = await node.BorrowRemoteModelClient().SendAnnProjectionAsync(destination, source,
                voterIndex, payload, mac.Sign(payload, reply: false), expiry, maximum, cancellationToken)
                .ConfigureAwait(false);
            await ServerFailureObserver.ObserveAsync(() =>
            {
                using var destinationMac = RemoteDocumentMac.FromConfiguredSecret(
                    RemoteModelNativeIdentity.Secret(options.Value, partition, destination.Owner));
                if (!destinationMac.Verify(reply.Payload.Span, reply.Signature, reply: true))
                { throw Errors.Fail(ErrorCode.Unauthenticated, AnnProjectionObservationProtocol.Invalid); }
                AnnProjectionObservationSource.RequireSame(partition, capturedSource, originalRequest, work);
                var grant = work.CreateReadGrant(work.RemainingReadGrantBytes, work.RemainingReadGrantRecords);
                try
                {
                    work.ImportReadGrant(grant, reply.Payload.Length, AnnProjectionObservationProtocol.NoReadWork);
                    var result = NativeSerialization.Deserialize<AnnProjectionObservationResult>(reply.Payload.Span);
                    if (result is null || result.Version != AnnProjectionObservationProtocol.Version
                        || result.NodeId == Guid.Empty || result.AcceptedReadBytes < AnnProjectionObservationProtocol.NoReadWork
                        || result.ExaminedRecords < AnnProjectionObservationProtocol.NoReadWork
                        || result.AcceptedReadBytes > call.Observation.MaximumReadBytes
                        || result.ExaminedRecords > call.Observation.MaximumRecords)
                    { throw Errors.Fail(ErrorCode.Unauthenticated, AnnProjectionObservationProtocol.Invalid); }
                    work.ImportReadGrant(grant, result.AcceptedReadBytes, result.ExaminedRecords);
                    work.Check();
                    observed = new(result, reply.DispatchSiloAddress);
                }
                finally { ServerFailureObserver.Observe(() => work.CompleteSettledReadGrant(grant), failures); }
                return Task.CompletedTask;
            }, failures).ConfigureAwait(false);
            ServerFailureObserver.Observe(() => ClearOwned(reply.Payload), failures);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(() => CryptographicOperations.ZeroMemory(payload), failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return observed ?? throw Errors.Fail(ErrorCode.RecoveryRequired, AnnProjectionObservationProtocol.Invalid);
    }

    internal bool RequireKnownLocalNonPublisher(RegisteredPhysicalOwnerV1 destination, int voterIndex,
        ReadExecutionBudget work)
    {
        if (!global::KeyLoad.Core.Features.ClusterRouting.Validation.PhysicalOwnerEntryValidation.SameOwner(
                destination.Owner, capturedSource.ConfiguredLocal.Owner)
            || destination.Owner.VoterIds[voterIndex] != partition.Configuration.LocalId)
        { return false; }
        AnnProjectionObservationSource.RequireSame(partition, capturedSource, originalRequest, work);
        if (capturedSource.SourceNodeId != partition.Database.Store.Identity.NodeId
            || capturedSource.SourceNodeId == originalRequest.NodeId)
        { throw Errors.Fail(ErrorCode.OwnershipLost, AnnProjectionObservationProtocol.Invalid); }
        return true;
    }

    private static void ClearOwned(ReadOnlyMemory<byte> original)
    {
        if (!MemoryMarshal.TryGetArray(original, out var buffer))
        { throw Errors.Fail(ErrorCode.Corruption, AnnProjectionObservationProtocol.Invalid); }
        CryptographicOperations.ZeroMemory(buffer.AsSpan());
    }
}
