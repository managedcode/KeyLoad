using System.Collections.Immutable;
using KeyLoad.Core;
using ManagedCode.Communication;

namespace KeyLoad.ProcessRecoveryTests.Search;

internal static class AnnCrashOperations
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
    internal static AnnMaintenanceRequest Seed(AnnCrashCommandOwner commands)
    {
        var database = commands.Database;
        var partition = AnnCrashContract.Partition;
        _ = Apply(commands, OperationKind.ConfigureResource, new ConfigureResourceRequest(partition.TenantId,
            partition.DatabaseId, new ResourceDefinition(AnnCrashContract.Collection, ResourceKind.Collection,
                partition.TransactionDomainId)), Guid.NewGuid()).Get<ResourceDefinition>();
        var mutations = new List<Mutation>();
        for (var index = FirstIndex; index < AnnCrashContract.Count; index++)
        {
            mutations.Add(new PutDocument(AnnCrashContract.Collection, AnnCrashContract.Id(index), DocumentJson));
            mutations.Add(Vector(index, ImmutableArray.Create(index + SeedFirstOffset, SeedSecond, SeedThird)));
        }
        var id = Guid.NewGuid();
        _ = Apply(commands, OperationKind.Batch, new CommandRequest(id, partition, [.. mutations]), id).Get<CommitReceipt>();
        var consumer = new ProjectionConsumerRef(partition, AnnCrashContract.Consumer);
        id = Guid.NewGuid();
        _ = Apply(commands, OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(id, consumer, new(AnnCrashContract.Generation, [], [.. Kinds]), InitialCheckpoint), id).Get<ProjectionConsumerInfo>();
        var identity = database.Store.Identity;
        return new(Guid.NewGuid(), consumer, AnnCrashContract.Collection, AnnCrashContract.Field,
            AnnCrashContract.Space, AnnCrashContract.Generation, database.Store.Identity.NodeId,
            new(identity.NodeId, identity.Incarnation), AnnMaintenanceMode.Build);
    }
    internal static PutVector Vector(int index, ImmutableArray<float> values)
        => new(AnnCrashContract.Collection, AnnCrashContract.Id(index), AnnCrashContract.Field,
            values, AnnCrashContract.Space, AnnCrashContract.Revision);
    internal static Result<object> Apply<T>(AnnCrashCommandOwner commands, OperationKind kind, T payload, Guid id)
        => commands.Submit(CrashDatabase.Operation(commands.Database, kind, payload, id), explicitTime: false);
    internal static ProjectionBatch Read(DatabaseEngine database, AnnMaintenanceRequest request, long upper)
        => database.ReadProjectionBatch(CrashFixtureValues.Principal,
            new(request.Consumer, AnnCrashContract.PageLimit, AnnCrashContract.PageBytes, upper));
    internal static ProjectionBatchResult Commit(AnnCrashCommandOwner commands, CommitProjectionBatchRequest intent)
        => Apply(commands, OperationKind.CommitProjectionBatch, intent, intent.CommandId).Get<ProjectionBatchResult>();
}
