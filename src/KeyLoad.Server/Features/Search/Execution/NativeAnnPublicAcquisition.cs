using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Server.Features.Search;

internal static class NativeAnnPublicAcquisition
{
    internal static IAnnProjectionLease Acquire(DatabaseEngine database, IKeyValueView view,
        NativeAnnGenerationOwner owner, AnnProjectionSelection request,
        ServerRuntimeOptions configured, ReadExecutionBudget budget)
        => new NativeAnnPublicReadLease(database, view, owner, request, configured, budget);

    internal static void RequireScope(AnnProjectionSelection request, NativeAnnManifest manifest)
    {
        if (manifest.Collection != request.Collection || manifest.Field != request.VectorField
            || manifest.Space != request.Space || manifest.Consumer.Partition != request.Partition)
        { throw Errors.Fail(ErrorCode.TokenInvalidated, NativeAnnProtocol.InvalidSource); }
    }

    internal static AnnMaintenanceRequest Request(NativeAnnManifest manifest)
    {
        var id = manifest.CheckpointIntent?.CommandId
            ?? throw Errors.Fail(ErrorCode.Corruption, NativeAnnProtocol.Corrupt);
        return new(id, manifest.Consumer, manifest.Collection, manifest.Field, manifest.Space,
            manifest.IndexGeneration, manifest.Source.NodeId, manifest.Placement, AnnMaintenanceMode.Restore);
    }
}
