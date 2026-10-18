using System.Collections.Immutable;
using KeyLoad.Server.Features.Search;

namespace KeyLoad.UnitTests.Features.Search;

/// <summary>Real canonical source/configure/child receipt; the index digest is only supplied codec test data.</summary>
internal sealed record NativeAnnSettledContractValues(AnnMaintenanceRequest Request,
    ProjectionConsumerInfo Configure, AnnMaintenanceResult Original, CommitProjectionBatchRequest Intent)
{
    private const string ConsumerName = "ann-original-wire";
    private const long Generation = 13;
    private const long StartAfter = 0;
    private const string SuppliedIndexDigest = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";

    internal static NativeAnnSettledContractValues Create(TestDatabase database, bool includeMaintenanceProfile = true)
    {
        var consumer = AnnProjectionPinTestSupport.Consumer(database, ConsumerName);
        var profile = includeMaintenanceProfile
            ? new AnnProjectionMaintenanceProfile(AnnSeedTestSupport.Collection, AnnSeedTestSupport.Field,
                AnnSeedTestSupport.Space()) : null;
        var configureId = Guid.NewGuid();
        ImmutableArray<string> resources = [];
        ImmutableArray<string> kinds = [MutationDiscriminatorNames.PutDocument, MutationDiscriminatorNames.PatchDocument,
            MutationDiscriminatorNames.DeleteDocument, MutationDiscriminatorNames.PutVector,
            MutationDiscriminatorNames.ApplyVectorProjection];
        var definition = new ProjectionConsumerDefinition(Generation, resources, kinds)
        {
            AnnMaintenance = profile
        };
        var configure = database.Submit(OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(configureId, consumer, definition, StartAfter),
            AnnProjectionPinTestSupport.Principal, configureId).Get<ProjectionConsumerInfo>();
        var placement = database.Database.ReadAtomicPartitionPlacement(AnnProjectionPinTestSupport.Principal,
            database.Partition);
        var request = new AnnMaintenanceRequest(Guid.NewGuid(), consumer, AnnSeedTestSupport.Collection,
            AnnSeedTestSupport.Field, AnnSeedTestSupport.Space(), Generation, database.Store.Identity.NodeId,
            new(placement.PhysicalShardId, placement.Incarnation, placement.VoterIds, placement.PlacementEpoch),
            AnnMaintenanceMode.Build)
        {
            MaintenanceProfile = profile
        };
        var captured = global::KeyLoad.Core.Features.Search.AnnSeedCollector.CapturePinned(database.Database,
            AnnProjectionPinTestSupport.Principal, request, UnitAnnSeedOptions.Execution(),
            new global::KeyLoad.Core.ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(database.Database.Limits)));
        var source = NativeAnnCanonicalSource.Source(captured);
        var page = AnnProjectionPinTestSupport.Read(database, consumer);
        var intent = NativeAnnMaintenanceTestData.Intent(page);
        var receipt = NativeAnnMaintenanceTestData.Commit(database, intent);
        var original = new AnnMaintenanceResult(request.CommandId, consumer, Generation,
            AnnMaintenancePhase.Completed, source, captured.Records.Length, SuppliedIndexDigest, receipt);
        return new(request, configure, original, intent);
    }
}
