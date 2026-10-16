using KeyLoad.Core;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Core.Features.Search;
using KeyLoad.Query;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace KeyLoad.Clustering;

internal static class AnnProjectionObservationRead
{
    internal static async Task<AnnProjectionObservationResult> ExecuteAsync(DatabaseEngine database,
        IServiceProvider services, TimeProvider clock, PrincipalRecord principal,
        DecodedGrainRequest original, IResourceLifetimeWorkBinding binding, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (original.Envelope.ReadKind == GrainReadKind.AnnProjectionObservationV2)
        {
            return await AnnProjectionLifetimeObservationRead.ExecuteAsync(database, services, clock,
                principal, original, binding, cancellationToken).ConfigureAwait(true);
        }
        cancellationToken.ThrowIfCancellationRequested();
        if (!services.GetRequiredService<IOptions<QueryExecutionOptions>>().Value.EnableApproximateSearch)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, AnnSearchProtocol.Disabled);
        }
        var request = GrainNativePayload.Read<AnnProjectionObservationRequest>(original.Payload);
        RequireRequest(request);
        if (request.Kind != AnnProjectionObservationKind.Identity)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, GrainRoutingProtocol.AdministrationRequired);
        }
        var work = new ReadExecutionBudget(database.OperationLimitsOptions, clock, cancellationToken);
        work.AttachResourceLifetimeBinding(binding);
        work.ConstrainLifetime(original.Envelope.ExpiresAt);
        work.ConstrainNativeWork(request.MaximumReadBytes, request.MaximumRecords);
        work.ChargeBytes(original.Payload.Length);
        var admitted = RequireScope(database, services, principal, request, work, binding: binding);
        var current = GrainRequestAuthority.ReloadForRequest(database, original.Envelope, clock);
        if (current.Id != principal.Id || current.PolicyEpoch != principal.PolicyEpoch)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProjectionObservationProtocol.Invalid);
        }
        if (RequireScope(database, services, current, request, work, binding: binding) != admitted)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProjectionObservationProtocol.Invalid);
        }
        var actual = database.Store.Identity;
        work.Check();
        var result = new AnnProjectionObservationResult(AnnProjectionObservationProtocol.Version, actual.NodeId, actual.ReadGeneration, null,
            work.ReadBytes, work.ConstrainedNativeRecords, null);
        work.CheckResult(result);
        return result;
    }

    internal static void RequireRequest(AnnProjectionObservationRequest request)
    {
        if (request is null || request.Version != AnnProjectionObservationProtocol.Version
            || !Enum.IsDefined(request.Kind) || request.OriginalRequest is null
            || request.OriginalRequest.CommandId == Guid.Empty || request.OriginalRequest.NodeId == Guid.Empty
            || request.OriginalRequest.Consumer is null || request.OriginalRequest.Consumer.Partition is null
            || request.OriginalRequest.Placement is null || request.OriginalRequest.Space is null
            || string.IsNullOrEmpty(request.OriginalRequest.Collection) || string.IsNullOrEmpty(request.OriginalRequest.Field)
            || request.Approximate is { Search: null }
            || request.OriginalRequest.IndexGeneration < AnnProjectionObservationProtocol.FirstGeneration
            || request.MaximumReadBytes <= AnnProjectionObservationProtocol.NoReadWork
            || request.MaximumRecords <= AnnProjectionObservationProtocol.NoReadWork
            || request.Kind == AnnProjectionObservationKind.Identity && (request.Wait is not null || request.Approximate is not null)
            || request.Kind == AnnProjectionObservationKind.Wait && (request.Wait is null || request.Approximate is not null)
            || request.Kind == AnnProjectionObservationKind.Approximate && (request.Approximate is null || request.Wait is not null))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, AnnProjectionObservationProtocol.Invalid);
        }
        var expected = request.OriginalRequest;
        if (request.Wait is { } wait && (wait.Consumer != expected.Consumer || wait.Partition != expected.Consumer.Partition
                || wait.Collection != expected.Collection || wait.VectorField != expected.Field
                || wait.Space != expected.Space || wait.IndexGeneration != expected.IndexGeneration)
            || request.Approximate is { } approximate && (approximate.Consumer != expected.Consumer
                || approximate.Search.Partition != expected.Consumer.Partition || approximate.Search.Collection != expected.Collection
                || approximate.Search.VectorField != expected.Field || approximate.Search.Space != expected.Space
                || approximate.IndexGeneration != expected.IndexGeneration))
        {
            throw Errors.Fail(ErrorCode.Unauthenticated, AnnProjectionObservationProtocol.Invalid);
        }
    }

    internal static LocalAnnObservationFence RequireScope(DatabaseEngine database, IServiceProvider services,
        PrincipalRecord principal, AnnProjectionObservationRequest request, ReadExecutionBudget work,
        AnnReadLifetimeMetadata? metadata = null, IResourceLifetimeWorkBinding? binding = null)
    {
        var original = request.OriginalRequest;
        var expected = services.GetRequiredService<PhysicalShardRecord>();
        if (!AnnProjectionObservationScope.SamePhysicalIdentity(expected, original.Placement)
            || request.Kind != AnnProjectionObservationKind.Identity && original.NodeId != database.Store.Identity.NodeId)
        {
            throw Errors.Fail(ErrorCode.OwnershipLost, AnnProjectionObservationProtocol.Invalid);
        }
        return binding is null ? database.CaptureLocalAnnObservation(principal.Id, original, work, metadata)
            : database.CaptureLocalAnnObservationBound(principal.Id, original, work, metadata, binding);
    }
}
