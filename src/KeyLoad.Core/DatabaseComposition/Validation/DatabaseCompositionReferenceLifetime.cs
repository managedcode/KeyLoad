using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private const string CompositionEndpointUnavailable = "The composition endpoint resource is unavailable.";

    private EntityRef BindCompositionInputRecord(IKeyValueView chargedView, EntityRef incoming, DocumentRecord visible)
    {
        var retained = visible.Reference;
        if (retained is null || retained.Partition != incoming.Partition
            || retained.Collection != incoming.Collection || retained.Id != incoming.Id)
        {
            throw Errors.Fail(ErrorCode.Corruption, TypedJoinRecordMismatch);
        }
        var definition = Resource(chargedView, incoming.Partition, incoming.Collection, ResourceKind.Collection);
        var actual = ResourceLifetimeCatalog.Read(chargedView, incoming.Partition.TenantId,
            incoming.Partition.DatabaseId, incoming.Collection, definition)
            ?? throw Errors.Fail(ErrorCode.NotFound, CompositionEndpointUnavailable);
        var bound = EntityReferenceLifetime.Bind(incoming, actual);
        EntityReferenceLifetime.RequireDocument(visible, actual);
        if (visible.Reference != bound)
        {
            throw Errors.Fail(ErrorCode.Corruption, TypedJoinRecordMismatch);
        }
        return bound;
    }
}
