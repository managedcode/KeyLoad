using KeyLoad.Core;
using KeyLoad.Core.Features.DocumentStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.DatabaseComposition;

/// <summary>Independent exact raw-read cost for the original fresh untyped one-message fixture.</summary>
internal static class DatabaseCompositionReadCost
{
    private const string ReadySpace = "ready";
    private const string MessageMetadataSpace = "message-meta";
    private const string MessageBodySpace = "message-body";
    private const long FirstReadySequence = 1L;
    private const int EndpointCatalogReads = 3;
    private const long MissingValueBytes = 0;
    private const string EndpointLifetimeMissing = "The original endpoint has no native catalog lifetime.";
    private const string RetirementStateUnsupported = "The exact fresh-fixture cost does not admit retirement state.";
    internal static long SingleUntyped(TestDatabase db, string queue, string message, EntityRef from, EntityRef to)
        => db.Store.Read(view =>
        {
            long Size(byte[] key) => key.LongLength + (view.ReadOwnedValue(key)?.LongLength ?? MissingValueBytes);
            long Endpoint(EntityRef reference)
            {
                var partition = reference.Partition;
                var definitionKey = KeySpace.Resource(partition.TenantId, partition.DatabaseId, reference.Collection);
                var definition = view.GetRecord<ResourceDefinition>(definitionKey);
                var lifetimeKey = ResourceLifetimeCatalog.Key(partition.TenantId, partition.DatabaseId, reference.Collection);
                var lifetime = ResourceLifetimeCatalog.Read(view, partition.TenantId, partition.DatabaseId, reference.Collection, definition)
                    ?? throw new InvalidOperationException(EndpointLifetimeMissing);
                var control = ResourceRetirementStorage.ControlKey(lifetime);
                var fence = ResourceRetirementStorage.FenceKey(lifetime);
                if (view.ReadOwnedValue(control) is not null || view.ReadOwnedValue(fence) is not null)
                {
                    throw new InvalidOperationException(RetirementStateUnsupported);
                }
                // BindAuthorizedReadResource: definition+life, absent control+fence.
                // VisibleVertex Bind and RequireStored each perform one further real life lookup.
                return Size(definitionKey) + EndpointCatalogReads * Size(lifetimeKey) + Size(control) + Size(fence)
                    + Size(DocumentStorageKeys.RecordKey(reference));
            }
            return Size(KeySpace.Partition(ReadySpace, db.Partition, queue, FirstReadySequence, message))
                + Size(KeySpace.Partition(MessageMetadataSpace, db.Partition, queue, message))
                + Size(KeySpace.Partition(MessageBodySpace, db.Partition, queue, message))
                + Endpoint(from) + Endpoint(to);
        });
}
