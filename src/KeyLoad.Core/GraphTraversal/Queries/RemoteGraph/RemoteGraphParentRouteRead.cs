using KeyLoad.Core.Features.ClusterRouting.Validation;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal RemoteGraphParentRouteV1 ReadRemoteGraphParentRoute(string principalId,
        PartitionRef source, EntityRef? destination, string graph, PhysicalShardRecord configuredControl,
        ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(work);
        ValidatePartition(source);
        JsonData.Identifier(graph);
        if (destination is not null)
        {
            ValidatePartition(destination.Partition);
            JsonData.Identifier(destination.Collection);
            JsonData.Identifier(destination.Id);
        }
        work.Check();
        var result = Store.Read(view =>
        {
            var bounded = work.CreateView(view);
            var principal = Principal(bounded, principalId, EvaluationClock.GetUtcNow());
            Authorization.Require(principal, source, graph, Capability.GraphWrite);
            _ = BindAuthorizedReadResourceMetadata(bounded, source.TenantId, source.DatabaseId,
                source.TransactionDomainId, graph, ResourceKind.Graph, work);
            var directory = ReadRemoteGraphControlDirectory(bounded, configuredControl);
            var actualSource = RouteRemoteGraphOwner(directory, ReadPlacementWitness(bounded, source));
            if (!PhysicalOwnerEntryValidation.SameOwner(actualSource.Owner, configuredControl))
            {
                throw Errors.Fail(ErrorCode.OwnershipLost, RemoteGraphProtocol.MissingAuthority);
            }
            var actualTarget = destination is null ? null : RouteRemoteGraphOwner(directory,
                ResolveRegisteredPlacement(destination.Partition, configuredControl));
            if (actualTarget is not null
                && PhysicalOwnerEntryValidation.SameOwner(actualSource.Owner, actualTarget.Owner))
            {
                throw Errors.Fail(ErrorCode.UnsupportedCapability, RemoteGraphProtocol.Invalid);
            }
            return new RemoteGraphParentRouteV1(actualSource, actualTarget, directory);
        });
        work.CheckResult(result);
        return result;
    }

    internal RemoteGraphIncomingRouteV1 ReadRemoteGraphIncomingRoute(string principalId,
        EntityRef target, string graph, PhysicalShardRecord configuredControl, ReadExecutionBudget work)
    {
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(work);
        ValidatePartition(target.Partition);
        JsonData.Identifier(graph);
        JsonData.Identifier(target.Collection);
        JsonData.Identifier(target.Id);
        work.Check();
        var result = Store.Read(view =>
        {
            var bounded = work.CreateView(view);
            var principal = Principal(bounded, principalId, EvaluationClock.GetUtcNow());
            Authorization.Require(principal, target.Partition, graph, Capability.GraphRead);
            Authorization.Require(principal, target.Partition, target.Collection, Capability.DocumentsRead);
            _ = BindAuthorizedReadResourceMetadata(bounded, target.Partition.TenantId, target.Partition.DatabaseId,
                target.Partition.TransactionDomainId, graph, ResourceKind.Graph, work);
            _ = BindAuthorizedReadResourceMetadata(bounded, target.Partition.TenantId, target.Partition.DatabaseId,
                target.Partition.TransactionDomainId, target.Collection, ResourceKind.Collection, work);
            var directory = ReadRemoteGraphControlDirectory(bounded, configuredControl);
            return new RemoteGraphIncomingRouteV1(RouteRemoteGraphOwner(directory,
                ResolveRegisteredPlacement(target.Partition, configuredControl)), directory);
        });
        work.CheckResult(result);
        return result;
    }
}
