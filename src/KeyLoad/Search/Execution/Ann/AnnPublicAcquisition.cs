using KeyLoad.Core;
using KeyLoad.Query.Features.Search;
using KeyLoad.Storage;

namespace KeyLoad.Server.Features.Search;

internal static class AnnPublicAcquisition
{
    internal static IAnnProjectionLease Acquire(DatabaseEngine database, IKeyValueView view,
        AnnGenerationOwner owner, AnnProjectionSelection request,
        ServerRuntimeOptions configured, ReadExecutionBudget budget)
        => new AnnPublicReadLease(database, view, owner, request, configured, budget);

    internal static void RequireScope(AnnProjectionSelection request, AnnManifest manifest)
    {
        if (manifest.Collection != request.Collection || manifest.Field != request.VectorField
            || manifest.Space != request.Space || manifest.Consumer.Partition != request.Partition)
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProtocol.InvalidSource);
        }
    }

    internal static AnnMaintenanceRequest Request(DatabaseEngine database, IKeyValueView view,
        AnnManifest manifest, ReadExecutionBudget budget)
    {
        var metadata = budget.CreateView(view);
        var principal = database.Principal(metadata, manifest.PrincipalId, database.EvaluationClock.GetUtcNow());
        var consumer = DatabaseEngine.RequireActiveProjectionConsumer(metadata, principal,
            manifest.Consumer, manifest.IndexGeneration);
        var profile = consumer.Definition.AnnMaintenance;
        if (profile is not null && (profile.Collection != manifest.Collection
            || profile.Field != manifest.Field || profile.Space != manifest.Space))
        {
            throw Errors.Fail(ErrorCode.TokenInvalidated, AnnProtocol.InvalidSource);
        }
        budget.Check();
        var id = manifest.CheckpointIntent?.CommandId
            ?? throw Errors.Fail(ErrorCode.Corruption, AnnProtocol.Corrupt);
        return new(id, manifest.Consumer, manifest.Collection, manifest.Field, manifest.Space,
            manifest.IndexGeneration, manifest.Source.NodeId, manifest.Placement, AnnMaintenanceMode.Restore)
        {
            MaintenanceProfile = profile
        };
    }
}
