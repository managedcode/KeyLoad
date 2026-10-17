using System.Security.Cryptography;
using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.Search;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.Server.Features.Search;

internal sealed class AnnProjectionNativeLifetimeReceiver(OrleansNode node, PartitionHost partition,
    ServerRuntimeOptions options, TimeProvider clock)
{
    internal async Task<(ReadOnlyMemory<byte> Payload, string Signature)> ExecuteAsync(
        RemoteModelNativeRequest original, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var source = RemoteDocumentMac.FromConfiguredSecret(
            RemoteModelNativeIdentity.Secret(options.Node.Value, partition, original.SourceOwner));
        if (!source.Verify(original.Payload.Span, original.Signature, reply: false))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, AnnProjectionObservationProtocol.Invalid);
        }
        var call = NativeSerialization.Deserialize<AnnProjectionNativeCallV2>(original.Payload.Span);
        Require(call, original, cancellationToken);
        var work = new ReadExecutionBudget(options.Core.DatabaseLimits, clock, cancellationToken);
        work.AttachResourceLifetimeBinding(binding);
        work.ConstrainLifetime(call.Original.ExpiresAt);
        work.ConstrainNativeWork(call.Original.Observation.MaximumReadBytes, call.Original.Observation.MaximumRecords);
        work.ChargeBytes(original.Payload.Length);
        var authority = ReadAuthority(call, work);
        var admitted = partition.Database.CaptureLocalAnnObservationBound(authority.Principal.Id,
            call.Original.Observation.OriginalRequest, work,
            new(call.Definition, call.Coverage, call.Principal, call.Definitions), binding);
        var encoded = AnnProjectionNativeLifetimeEncoding.EncodeChild(call, work, partition.Database.Limits.MaxBatchBytes);
        var input = encoded.Bytes;
        try
        {
            var result = await AnnProjectionNativeLifetimeQuery.ExecuteAsync(node, partition, clock, authority.Principal,
                admitted, encoded.Call, input, work, binding, cancellationToken).ConfigureAwait(false);
            Require(call, original, cancellationToken);
            var current = ReadAuthority(call, work);
            if (current.Principal.PolicyEpoch != authority.Principal.PolicyEpoch || current.Lifetime != authority.Lifetime)
            {
                throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProjectionObservationProtocol.Invalid);
            }
            RequireObservation(authority.Principal.Id, call, admitted, work, binding);
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
            var bytes = AnnProjectionNativeEncoding.Encode(result, work, options.GrainRouting.Value.MaximumCompletedBytes);
            return SignReply(bytes, original);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(input);
        }
    }

    private void RequireObservation(string principalId, AnnProjectionNativeCallV2 call,
        LocalAnnObservationFence admitted, ReadExecutionBudget work, IResourceLifetimeWorkBinding binding)
    {
        var current = partition.Database.CaptureLocalAnnObservationBound(principalId,
            call.Original.Observation.OriginalRequest, work,
            new(call.Definition, call.Coverage, call.Principal, call.Definitions), binding);
        if (current != admitted)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProjectionObservationProtocol.Invalid);
        }
    }

    private (PrincipalRecord Principal, ResourceLifetimeV1 Lifetime) ReadAuthority(AnnProjectionNativeCallV2 call,
        ReadExecutionBudget work)
        => partition.Database.Store.Read(view =>
        {
            var request = call.Original.Observation.OriginalRequest;
            var bounded = work.CreateView(view);
            var principal = partition.Database.Principal(bounded, call.Original.PrincipalId, clock.GetUtcNow());
            partition.Database.Authorization.Require(principal, request.Consumer.Partition, request.Collection, Capability.VectorSearch);
            var resource = partition.Database.BindAuthorizedReadResource(bounded, request.Consumer.Partition, request.Collection, ResourceKind.Collection);
            var lifetime = AnnLifetimeReadAuthority.Require(partition.Database, view, principal, resource,
                request.Consumer.Partition, request.Collection, request.Field,
                new(call.Definition, call.Coverage, call.Principal, call.Definitions), work);
            return (principal, lifetime);
        });

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

    private void Require(AnnProjectionNativeCallV2 call, RemoteModelNativeRequest original, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        if (call?.Original is not { } inner || inner.Version != AnnProjectionObservationProtocol.Version
            || inner.Observation?.OriginalRequest is null || call.Definition is null || call.Coverage is null || call.Principal is null
            || inner.ExpiresAt != original.ExpiresAt || inner.ExpiresAt <= clock.GetUtcNow()
            || string.IsNullOrWhiteSpace(inner.PrincipalId)
            || !AnnProjectionObservationScope.SamePhysicalIdentity(original.DestinationOwner, inner.Observation.OriginalRequest.Placement))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, AnnProjectionObservationProtocol.Invalid);
        }
        JsonData.Identifier(inner.PrincipalId);
    }
}
