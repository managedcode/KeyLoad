using System.Collections.Immutable;
using KeyLoad.Core.Features.GraphTraversal;
using KeyLoad.Core.Features.GraphTraversal.Serialization;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

internal static class RemoteGraphLifetimeAdmission
{
    private const int PreparationResourceCount = 2;
    private const int MaximumIntentResourceCount = 4;

    internal static ImmutableArray<ResourceLifetimeV1> Capture(IKeyValueView view, PartitionRef partition,
        string graph, string collection, DatabaseLimits limits, bool requireOpen = false)
    {
        var graphDefinition = DatabaseEngine.BindResourceDefinition(view, partition, graph, ResourceKind.Graph);
        var collectionDefinition = DatabaseEngine.BindResourceDefinition(view, partition, collection, ResourceKind.Collection);
        var values = ImmutableArray.Create(
            ResourceLifetimeCatalog.Read(view, partition.TenantId, partition.DatabaseId, graph, graphDefinition)!,
            ResourceLifetimeCatalog.Read(view, partition.TenantId, partition.DatabaseId, collection, collectionDefinition)!);
        if (requireOpen)
        {
            foreach (var lifetime in values)
            {
                ResourceRetirementStorage.RequireOpen(view, lifetime, limits);
            }
        }
        return values;
    }

    internal static EntityRef BindDestination(RemoteGraphTargetPreparationV1 preparation)
    {
        RequirePreparation(preparation);
        var reference = preparation.Request.Destination;
        var actual = preparation.ResourceLifetimes.Single(value => Matches(value, reference.Partition,
            reference.Collection, ResourceKind.Collection));
        return EntityReferenceLifetime.Bind(reference, actual);
    }

    internal static EntityRef BindOriginalDestination(RemoteGraphIntentV1 original, EntityRef requested)
    {
        var reference = original.Content.Destination;
        if (original.ResourceLifetimes.IsDefaultOrEmpty || original.ResourceLifetimes.Length > MaximumIntentResourceCount)
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
        }
        EntityReferenceLifetime.RequireBound(reference);
        var selected = original.ResourceLifetimes.Where(value => Matches(value, reference.Partition,
            reference.Collection, ResourceKind.Collection)).ToArray();
        if (selected.Length != OneReferenceLifetime)
        {
            throw Errors.Fail(ErrorCode.Corruption, RemoteGraphProtocol.Invalid);
        }
        EntityReferenceLifetime.RequireStored(reference, selected[FirstReferenceLifetime]);
        return EntityReferenceLifetime.Bind(requested, selected[FirstReferenceLifetime]);
    }

    private const int OneReferenceLifetime = 1;
    private const int FirstReferenceLifetime = 0;

    internal static void RequirePreparation(RemoteGraphTargetPreparationV1 preparation)
    {
        var values = preparation.ResourceLifetimes;
        var request = preparation.Request;
        if (values.IsDefault || values.Length != PreparationResourceCount
            || !values.Any(value => Matches(value, request.Destination.Partition, request.Graph, ResourceKind.Graph))
            || !values.Any(value => Matches(value, request.Destination.Partition, request.Destination.Collection, ResourceKind.Collection)))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteGraphProtocol.Invalid);
        }
        foreach (var value in values)
        {
            ResourceOwnerInventoryValidation.Lifetime(value);
        }
    }

    internal static void RequireReceiver(IKeyValueView view, RemoteGraphIntentV1 intent, DatabaseLimits limits)
    {
        var content = intent.Content;
        var values = intent.ResourceLifetimes;
        if (values.IsDefaultOrEmpty || values.Length > MaximumIntentResourceCount
            || !values.Any(value => Matches(value, content.SourcePartition, content.Graph, ResourceKind.Graph))
            || !values.Any(value => Matches(value, content.Edge.From.Partition, content.Edge.From.Collection, ResourceKind.Collection)))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteGraphProtocol.Invalid);
        }
        var local = Capture(view, content.Destination.Partition, content.Graph, content.Destination.Collection, limits);
        if (local.Any(value => !values.Contains(value)))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, RemoteGraphProtocol.Invalid);
        }
        foreach (var value in values)
        {
            ResourceOwnerInventoryValidation.Lifetime(value);
            if (view is IResourceEffectObservation observation)
            {
                observation.ObserveLifetime(value);
            }
        }
    }

    private static bool Matches(ResourceLifetimeV1 value, PartitionRef partition, string name, ResourceKind kind)
        => value is not null && value.TenantId == partition.TenantId && value.DatabaseId == partition.DatabaseId
            && value.TransactionDomainId == partition.TransactionDomainId && value.ResourceName == name && value.Kind == kind;
}
