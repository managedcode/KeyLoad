using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal RemoteGraphTargetPreparationV1 ReadRemoteGraphTargetPreparation(string principalId,
        RemoteGraphPreparationRequestV1 request, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(work);
        work.Check();
        if (request.OriginalCommandId == Guid.Empty || request.PrincipalId != principalId
            || request.Destination is null || request.SourcePartition is null
            || request.SourceOwner is null || !PhysicalOwnerEntryValidation.Valid(request.DestinationOwner))
        { throw Errors.Fail(ErrorCode.Validation, RemoteGraphProtocol.Invalid); }
        ValidatePartition(request.SourcePartition);
        ValidatePartition(request.Destination.Partition);
        JsonData.Identifier(request.Graph);
        JsonData.Identifier(request.Destination.Collection);
        JsonData.Identifier(request.Destination.Id);
        if (request.SourcePartition == request.Destination.Partition
            || PhysicalOwnerEntryValidation.SameOwner(request.SourceOwner, request.DestinationOwner.Owner))
        { throw Errors.Fail(ErrorCode.Validation, RemoteGraphProtocol.Invalid); }
        var result = Store.Read(view => CaptureRemoteGraphTarget(work.CreateView(view), principalId, request, work));
        work.CheckResult(result);
        return result;
    }

    private RemoteGraphTargetPreparationV1 CaptureRemoteGraphTarget(IKeyValueView view, string principalId,
        RemoteGraphPreparationRequestV1 request, ReadExecutionBudget work)
    {
        var principal = Principal(view, principalId, EvaluationClock.GetUtcNow());
        Authorization.Require(principal, request.Destination.Partition, request.Graph, Capability.GraphWrite);
        var graph = Resource(view, request.Destination.Partition, request.Graph, ResourceKind.Graph);
        var cut = CaptureRemoteGraphOwnerCut(view, principal, request.Destination.Partition,
            request.DestinationOwner.Owner, graph.SchemaVersion);
        long? documentRevision = null;
        var collection = Resource(view, request.Destination.Partition, request.Destination.Collection,
            ResourceKind.Collection);
        if (!request.Deleted)
        { documentRevision = VisibleVertex(view, principal, request.Destination, work).Revision; }
        work.Check();
        return new(RemoteGraphProtocol.Version, request, cut, collection.SchemaVersion, documentRevision);
    }
}
