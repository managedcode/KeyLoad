using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    internal EntityRef BindEntityReference(IKeyValueView view, EntityRef reference)
    {
        var definition = Resource(view, reference.Partition, reference.Collection, ResourceKind.Collection);
        return EntityReferenceLifetime.Bind(view, reference, definition);
    }

    internal void RequireStoredEntityReference(IKeyValueView view, EntityRef reference)
    {
        var definition = Resource(view, reference.Partition, reference.Collection, ResourceKind.Collection);
        EntityReferenceLifetime.RequireStored(view, reference, definition);
    }
}
