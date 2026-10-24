using System.Collections.Immutable;
using KeyLoad.Core.DocumentStorage;
using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core.AnalyticsStore;

internal static class AnalyticsStoreLinks
{
    private const string Unavailable = "The analytics event reference is unavailable.";

    internal static void Require(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, ImmutableArray<EntityRef> links)
        => _ = Bind(database, view, principal, partition, links);

    internal static ImmutableArray<EntityRef> Bind(DatabaseEngine database, IKeyValueView view,
        PrincipalRecord principal, PartitionRef partition, ImmutableArray<EntityRef> links)
    {
        RequireShape(links);
        var bound = ImmutableArray.CreateBuilder<EntityRef>(links.Length);
        var unique = new HashSet<EntityRef>();
        foreach (var link in links)
        {
            var actual = Resolve(database, view, principal, partition, link, stored: false);
            if (!unique.Add(actual))
            {
                throw Errors.Fail(ErrorCode.Validation, AnalyticsStoreValidation.Invalid);
            }
            bound.Add(actual);
        }
        return bound.MoveToImmutable();
    }

    internal static void RequireStored(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, ImmutableArray<EntityRef> links)
    {
        RequireShape(links);
        foreach (var link in links)
        {
            _ = Resolve(database, view, principal, partition, link, stored: true);
        }
    }

    private static void RequireShape(ImmutableArray<EntityRef> links)
    {
        if (links.IsDefault || links.Any(link => link is null) || links.Distinct().Count() != links.Length)
        {
            throw Errors.Fail(ErrorCode.Validation, AnalyticsStoreValidation.Invalid);
        }
    }

    private static EntityRef Resolve(DatabaseEngine database, IKeyValueView view, PrincipalRecord principal,
        PartitionRef partition, EntityRef link, bool stored)
    {
        if (link.Partition != partition)
        {
            throw Errors.Fail(ErrorCode.UnsupportedCapability, Unavailable);
        }
        JsonData.Identifier(link.Collection);
        JsonData.Identifier(link.Id);
        try
        {
            database.Authorization.Require(principal, partition, link.Collection, Capability.DocumentsRead);
            var definition = stored
                ? ResourceLifetimeReadBinding.BindAuthorizedReadResource(view, partition, link.Collection, ResourceKind.Collection)
                : database.Resource(view, partition, link.Collection, ResourceKind.Collection);
            var lifetime = ResourceLifetimeCatalog.Read(view, partition.TenantId, partition.DatabaseId,
                link.Collection, definition) ?? throw Errors.Fail(ErrorCode.NotFound, Unavailable);
            EntityRef bound;
            if (stored)
            {
                EntityReferenceLifetime.RequireStored(link, lifetime);
                bound = link;
            }
            else
            {
                bound = EntityReferenceLifetime.Bind(link, lifetime);
            }
            var row = view.GetRecord<DocumentRecord>(DocumentStorageKeys.RecordKey(partition, link.Collection, link.Id));
            EntityReferenceLifetime.RequireDocument(row, lifetime);
            if (row is null || row.Deleted || !database.Authorization.CanReadRow(principal, row.Access))
            {
                throw Errors.Fail(ErrorCode.PermissionDenied, Unavailable);
            }
            return bound;
        }
        catch (KeyLoadException error) when (error.Code is ErrorCode.NotFound or ErrorCode.PermissionDenied)
        {
            throw Errors.Fail(ErrorCode.PermissionDenied, Unavailable);
        }
    }
}
