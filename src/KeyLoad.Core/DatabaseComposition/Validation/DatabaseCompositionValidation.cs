using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes DatabaseCompositionValidation operations against the caller's database context.</summary>
public static class DatabaseCompositionValidation
{
    internal const string CompositionEndpointUnavailable = "The composition endpoint resource is unavailable.";
    internal static EntityRef BindCompositionInputRecord(this DatabaseEngine engine, IKeyValueView chargedView, EntityRef incoming, DocumentRecord visible)
    {
        var retained = visible.Reference;
        if (retained is null || retained.Partition != incoming.Partition || retained.Collection != incoming.Collection || retained.Id != incoming.Id)
        {
            throw Errors.Fail(ErrorCode.Corruption, DatabaseCompositionExecutionDatabaseCompositionTypedEndpointJoin.TypedJoinRecordMismatch);
        }

        var definition = engine.Resource(chargedView, incoming.Partition, incoming.Collection, ResourceKind.Collection);
        var actual = ResourceLifetimeCatalog.Read(chargedView, incoming.Partition.TenantId, incoming.Partition.DatabaseId, incoming.Collection, definition) ?? throw Errors.Fail(ErrorCode.NotFound, CompositionEndpointUnavailable);
        var bound = EntityReferenceLifetime.Bind(incoming, actual);
        EntityReferenceLifetime.RequireDocument(visible, actual);
        if (visible.Reference != bound)
        {
            throw Errors.Fail(ErrorCode.Corruption, DatabaseCompositionExecutionDatabaseCompositionTypedEndpointJoin.TypedJoinRecordMismatch);
        }

        return bound;
    }
}
