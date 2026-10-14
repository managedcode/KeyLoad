using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.Search;

internal sealed class AnnProjectionNativeReceiver(OrleansNode node, PartitionHost partition,
    ServerRuntimeOptions options, TimeProvider clock)
{
    internal async Task<(ReadOnlyMemory<byte> Payload, string Signature)> ExecuteAsync(
        RemoteModelNativeRequest original, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var source = RemoteDocumentMac.FromConfiguredSecret(
            RemoteModelNativeIdentity.Secret(options.Node.Value, partition, original.SourceOwner));
        if (!source.Verify(original.Payload.Span, original.Signature, reply: false))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, AnnProjectionObservationProtocol.Invalid);
        }
        var call = NativeSerialization.Deserialize<AnnProjectionNativeCall>(original.Payload.Span);
        Require(call, original, cancellationToken);
        var work = new ReadExecutionBudget(options.Core.DatabaseLimits, clock, cancellationToken);
        work.ConstrainLifetime(call.ExpiresAt);
        work.ConstrainNativeWork(call.Observation.MaximumReadBytes, call.Observation.MaximumRecords);
        work.ChargeBytes(original.Payload.Length);
        var principal = partition.Database.Store.Read(view =>
            partition.Database.Principal(work.CreateView(view), call.PrincipalId, clock.GetUtcNow()));
        var admitted = partition.Database.CaptureLocalAnnObservation(principal.Id,
            call.Observation.OriginalRequest, work);
        var encoded = AnnProjectionNativeEncoding.EncodeChild(call, work, partition.Database.Limits.MaxBatchBytes);
        var child = encoded.Call;
        var input = encoded.Bytes;
        try
        {
            var result = await AnnProjectionNativeQuery.ExecuteAsync(node, partition, clock, principal, admitted, child, input,
                work, cancellationToken).ConfigureAwait(false);
            Require(call, original, cancellationToken);
            var current = partition.Database.Store.Read(view =>
                partition.Database.Principal(work.CreateView(view), call.PrincipalId, clock.GetUtcNow()));
            if (current.PolicyEpoch != principal.PolicyEpoch)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProjectionObservationProtocol.Invalid);
            }
            partition.Database.RequireLocalAnnObservation(principal.Id, call.Observation.OriginalRequest, admitted, work);
            var actual = partition.Database.Store.Identity;
            if (result.NodeId != actual.NodeId || result.ReadGeneration != actual.ReadGeneration)
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, AnnProjectionObservationProtocol.Invalid);
            }
            result = result with
            {
                AcceptedReadBytes = work.ReadBytes,
                ExaminedRecords = work.ConstrainedNativeRecords
            };
            var bytes = AnnProjectionNativeEncoding.Encode(result, work,
                options.GrainRouting.Value.MaximumCompletedBytes);
            return SignReply(bytes, original);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    private (ReadOnlyMemory<byte> Payload, string Signature) SignReply(byte[] bytes, RemoteModelNativeRequest original)
    {
        try
        {
            using var destination = RemoteDocumentMac.FromConfiguredSecret(
                RemoteModelNativeIdentity.Secret(options.Node.Value, partition, original.DestinationOwner));
            return (bytes, destination.Sign(bytes, reply: true));
        }
        catch (Exception failure) when (NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw;
        }
        catch (Exception failure) when (!NativeCqrsBoundaryErrors.IsNonFatal(failure))
        {
            CryptographicOperations.ZeroMemory(bytes);
            throw;
        }
    }

    private void Require(AnnProjectionNativeCall call, RemoteModelNativeRequest original,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (call is null || call.Version != AnnProjectionObservationProtocol.Version
            || call.Observation is null || call.Observation.OriginalRequest is null
            || call.ExpiresAt != original.ExpiresAt || call.ExpiresAt <= clock.GetUtcNow()
            || string.IsNullOrWhiteSpace(call.PrincipalId)
            || !AnnProjectionObservationScope.SamePhysicalIdentity(
                original.DestinationOwner, call.Observation.OriginalRequest.Placement))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, AnnProjectionObservationProtocol.Invalid);
        }
        JsonData.Identifier(call.PrincipalId);
    }
}
