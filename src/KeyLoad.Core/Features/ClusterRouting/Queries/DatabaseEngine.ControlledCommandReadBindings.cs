using KeyLoad.Core.Features.BlobStorage;
using KeyLoad.Core.Features.ResourceExecution;
using KeyLoad.Storage;

namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    private void BindControlledDocumentReadDependencies(IKeyValueView view, CommandRequest command)
    {
        if (view is not IResourceLifetimeReadBindingView)
        {
            return;
        }
        foreach (var effect in command.Mutations.DistinctBy(static effect => effect.Resource))
        {
            BindControlledCommandReadDependency(view, command.Partition, effect.Resource, ResourceKind.Collection);
        }
    }

    private void BindControlledBlobReadDependency(IKeyValueView view, ReplicatedOperation original)
    {
        if (view is not IResourceLifetimeReadBindingView)
        {
            return;
        }
        var blob = BlobCommandScope.From(original).Blob;
        BindControlledCommandReadDependency(view, blob.Partition, blob.Resource, ResourceKind.BlobStore);
    }

    private void BindControlledCommandReadDependency(IKeyValueView view, PartitionRef partition,
        string name, ResourceKind kind)
    {
        var pair = ReadBoundResourcePair(view, partition, name, kind);
        BindAuthenticatedReadResourceLifetime(view, partition, pair.Definition, pair.Lifetime);
    }
}
