using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Search;

internal static class TextIncrementalCrashSeed
{
    private const string PutDocumentKind = "putDocument";
    private const string PatchDocumentKind = "patchDocument";
    private const string DeleteDocumentKind = "deleteDocument";

    internal static async Task<TextIncrementalCrashOriginal> PrepareAsync(
        TextIncrementalCrashRuntime runtime, CancellationToken token)
    {
        var partition = TextIncrementalCrashProtocol.Partition;
        var creationId = Guid.NewGuid();
        _ = await runtime.CommitAsync<ResourceDefinition>(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId,
                new(TextIncrementalCrashProtocol.Collection, ResourceKind.Collection, partition.TransactionDomainId)),
            creationId, token);
        var creation = runtime.Store.Read(view => view.GetRecord<StoredOutcome>(
            OutcomeStoreOracle.GlobalKey(CrashFixtureValues.Principal, creationId)))
            ?? throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
        if (!creation.Result.IsSuccess || creation.PreparedCommand.CommandId != creationId)
        {
            throw new InvalidOperationException(TextIncrementalCrashProtocol.Invalid);
        }
        await TextIncrementalEvidenceFiles.WriteAsync(runtime.EvidenceRoot,
            TextIncrementalCrashProtocol.CreationFile, creation, token);
        var seed = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(TextIncrementalCrashProtocol.Collection, TextIncrementalCrashProtocol.Ukrainian,
                TextIncrementalCrashProtocol.InitialUkrainianJson),
                new PutDocument(TextIncrementalCrashProtocol.Collection, TextIncrementalCrashProtocol.English,
                    TextIncrementalCrashProtocol.InitialEnglishJson)]);
        _ = await runtime.CommitAsync<CommitReceipt>(OperationKind.Batch, seed, seed.CommandId, token);
        var consumer = new ProjectionConsumerRef(partition, TextIncrementalCrashProtocol.Consumer);
        var configureId = Guid.NewGuid();
        _ = await runtime.CommitAsync<ProjectionConsumerInfo>(OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(configureId, consumer,
                new(TextIncrementalCrashProtocol.Generation, [TextIncrementalCrashProtocol.Collection],
                    [PutDocumentKind, PatchDocumentKind, DeleteDocumentKind])), configureId, token);
        var identity = runtime.Database.Store.Identity;
        var request = new TextIndexMaintenanceRequest(Guid.NewGuid(), consumer,
            TextIncrementalCrashProtocol.Collection, TextIncrementalCrashProtocol.Field,
            TextIncrementalCrashProtocol.Generation, runtime.Database.Store.Identity.NodeId,
            new(identity.NodeId, identity.Incarnation),
            TextIndexMaintenanceMode.Build);
        _ = await TextIncrementalCrashReplay.FinishAsync(runtime, request, token);
        var mutation = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(TextIncrementalCrashProtocol.Collection, TextIncrementalCrashProtocol.Ukrainian,
                TextIncrementalCrashProtocol.ChangedJson, ExpectedRevision: TextIncrementalCrashProtocol.Revision),
                new DeleteDocument(TextIncrementalCrashProtocol.Collection, TextIncrementalCrashProtocol.English,
                    ExpectedRevision: TextIncrementalCrashProtocol.Revision)]);
        var receipt = await runtime.CommitAsync<CommitReceipt>(OperationKind.Batch, mutation, mutation.CommandId, token);
        return new(request with
        {
            Mode = TextIndexMaintenanceMode.Restore
        }, mutation, receipt);
    }
}
