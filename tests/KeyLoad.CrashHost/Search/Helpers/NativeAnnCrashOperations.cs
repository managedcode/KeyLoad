using System.Collections.Immutable;
using KeyLoad.Core;

namespace KeyLoad.CrashHost.Features.Search;

internal static class NativeAnnCrashOperations
{
    private const string PutDocumentKind = "putDocument";
    private const string PatchDocumentKind = "patchDocument";
    private const string DeleteDocumentKind = "deleteDocument";
    private const string PutVectorKind = "putVector";
    private const string ProjectionKind = "applyVectorProjection";
    private const string DocumentJson = "{}";
    private const int FirstIndex = 0;
    private const long InitialCheckpoint = 0;
    private const float SeedFirstOffset = 0.25f;
    private const float SeedSecond = 1f;
    private const float SeedThird = -0.5f;
    private static readonly string[] Kinds = [PutDocumentKind, PatchDocumentKind, DeleteDocumentKind, PutVectorKind, ProjectionKind];
    internal static AnnMaintenanceRequest Seed(NativeAnnCrashCommandOwner commands)
    {
        var database = commands.Database;
        var partition = NativeAnnCrashContract.Partition;
        _ = Apply(commands, OperationKind.ConfigureResource, new ConfigureResourceRequest(partition.TenantId,
            partition.DatabaseId, new ResourceDefinition(NativeAnnCrashContract.Collection, ResourceKind.Collection,
                partition.TransactionDomainId)), Guid.NewGuid()).Get<ResourceDefinition>();
        var mutations = new List<Mutation>();
        for (var index = FirstIndex; index < NativeAnnCrashContract.Count; index++)
        {
            mutations.Add(new PutDocument(NativeAnnCrashContract.Collection, NativeAnnCrashContract.Id(index), DocumentJson));
            mutations.Add(Vector(index, ImmutableArray.Create(index + SeedFirstOffset, SeedSecond, SeedThird)));
        }
        var id = Guid.NewGuid();
        _ = Apply(commands, OperationKind.Batch, new CommandRequest(id, partition, [.. mutations]), id).Get<CommitReceipt>();
        var consumer = new ProjectionConsumerRef(partition, NativeAnnCrashContract.Consumer);
        id = Guid.NewGuid();
        _ = Apply(commands, OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(id, consumer, new(NativeAnnCrashContract.Generation, [], [.. Kinds]), InitialCheckpoint), id).Get<ProjectionConsumerInfo>();
        var placement = database.ReadAtomicPartitionPlacement(CrashFixtureValues.Principal, new(NativeAnnCrashContract.Version, partition));
        return new(Guid.NewGuid(), consumer, NativeAnnCrashContract.Collection, NativeAnnCrashContract.Field,
            NativeAnnCrashContract.Space, NativeAnnCrashContract.Generation, database.Store.Identity.NodeId,
            new(placement.PhysicalShardId, placement.Incarnation, placement.VoterIds, placement.PlacementEpoch), AnnMaintenanceMode.Build);
    }
    internal static PutVector Vector(int index, ImmutableArray<float> values)
        => new(NativeAnnCrashContract.Collection, NativeAnnCrashContract.Id(index), NativeAnnCrashContract.Field,
            values, NativeAnnCrashContract.Space, NativeAnnCrashContract.Revision);
    internal static OperationResult Apply<T>(NativeAnnCrashCommandOwner commands, OperationKind kind, T payload, Guid id)
        => commands.Submit(CrashDatabase.Operation(kind, payload, id), explicitTime: false);
    internal static ProjectionBatch Read(DatabaseEngine database, AnnMaintenanceRequest request, long upper)
        => database.ReadProjectionBatch(CrashFixtureValues.Principal,
            new(request.Consumer, NativeAnnCrashContract.PageLimit, NativeAnnCrashContract.PageBytes, upper));
    internal static ProjectionBatchResult Commit(NativeAnnCrashCommandOwner commands, CommitProjectionBatchRequest intent)
        => Apply(commands, OperationKind.CommitProjectionBatch, intent, intent.CommandId).Get<ProjectionBatchResult>();
}
