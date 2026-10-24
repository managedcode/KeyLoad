using KeyLoad.Core.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;
/// <summary>Executes DocumentStorageExecutionEntityReferenceLifetime operations against the caller's database context.</summary>
public static class DocumentStorageExecutionEntityReferenceLifetime
{
    internal static EntityRef BindEntityReference(this DatabaseEngine engine, IKeyValueView view, EntityRef reference)
    {
        var definition = engine.Resource(view, reference.Partition, reference.Collection, ResourceKind.Collection);
        return EntityReferenceLifetime.Bind(view, reference, definition);
    }

    internal static void RequireStoredEntityReference(this DatabaseEngine engine, IKeyValueView view, EntityRef reference)
    {
        var definition = engine.Resource(view, reference.Partition, reference.Collection, ResourceKind.Collection);
        EntityReferenceLifetime.RequireStored(view, reference, definition);
    }
}
