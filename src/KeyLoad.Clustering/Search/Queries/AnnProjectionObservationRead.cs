using KeyLoad.Core;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Core.Search;
using Microsoft.Extensions.DependencyInjection;

namespace KeyLoad.Clustering;

internal static partial class AnnProjectionObservationRead
{
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
