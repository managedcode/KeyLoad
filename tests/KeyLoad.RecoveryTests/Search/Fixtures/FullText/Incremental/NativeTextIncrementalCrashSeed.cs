using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.RecoveryTests.Features.Search;

internal static class NativeTextIncrementalCrashSeed
{
    private const string PutDocumentKind = "putDocument";
    private const string PatchDocumentKind = "patchDocument";
    private const string DeleteDocumentKind = "deleteDocument";

    internal static async Task<NativeTextIncrementalCrashOriginal> PrepareAsync(
        NativeTextIncrementalCrashRuntime runtime, CancellationToken token)
    {
        var partition = NativeTextIncrementalCrashProtocol.Partition;
        var creationId = Guid.NewGuid();
        _ = await runtime.CommitAsync<ResourceDefinition>(OperationKind.ConfigureResource,
            new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId,
                new(NativeTextIncrementalCrashProtocol.Collection, ResourceKind.Collection, partition.TransactionDomainId)),
            creationId, token);
        var creation = runtime.Store.Read(view => view.GetRecord<StoredOutcome>(
            OutcomeStoreOracle.GlobalKey(CrashFixtureValues.Principal, creationId)))
            ?? throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid);
        if (!creation.Result.IsSuccess || creation.PreparedCommand.CommandId != creationId)
        {
            throw new InvalidOperationException(NativeTextIncrementalCrashProtocol.Invalid);
        }
        await NativeTextIncrementalEvidenceFiles.WriteAsync(runtime.EvidenceRoot,
            NativeTextIncrementalCrashProtocol.CreationFile, creation, token);
        var seed = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(NativeTextIncrementalCrashProtocol.Collection, NativeTextIncrementalCrashProtocol.Ukrainian,
                NativeTextIncrementalCrashProtocol.InitialUkrainianJson),
                new PutDocument(NativeTextIncrementalCrashProtocol.Collection, NativeTextIncrementalCrashProtocol.English,
                    NativeTextIncrementalCrashProtocol.InitialEnglishJson)]);
        _ = await runtime.CommitAsync<CommitReceipt>(OperationKind.Batch, seed, seed.CommandId, token);
        var consumer = new ProjectionConsumerRef(partition, NativeTextIncrementalCrashProtocol.Consumer);
        var configureId = Guid.NewGuid();
        _ = await runtime.CommitAsync<ProjectionConsumerInfo>(OperationKind.ConfigureProjectionConsumer,
            new ConfigureProjectionConsumerRequest(configureId, consumer,
                new(NativeTextIncrementalCrashProtocol.Generation, [NativeTextIncrementalCrashProtocol.Collection],
                    [PutDocumentKind, PatchDocumentKind, DeleteDocumentKind])), configureId, token);
        var identity = runtime.Database.Store.Identity;
        var request = new TextIndexMaintenanceRequest(Guid.NewGuid(), consumer,
            NativeTextIncrementalCrashProtocol.Collection, NativeTextIncrementalCrashProtocol.Field,
            NativeTextIncrementalCrashProtocol.Generation, runtime.Database.Store.Identity.NodeId,
            new(identity.NodeId, identity.Incarnation),
            TextIndexMaintenanceMode.Build);
        _ = await NativeTextIncrementalCrashReplay.FinishAsync(runtime, request, token);
        var mutation = new CommandRequest(Guid.NewGuid(), partition,
            [new PutDocument(NativeTextIncrementalCrashProtocol.Collection, NativeTextIncrementalCrashProtocol.Ukrainian,
                NativeTextIncrementalCrashProtocol.ChangedJson, ExpectedRevision: NativeTextIncrementalCrashProtocol.Revision),
                new DeleteDocument(NativeTextIncrementalCrashProtocol.Collection, NativeTextIncrementalCrashProtocol.English,
                    ExpectedRevision: NativeTextIncrementalCrashProtocol.Revision)]);
        var receipt = await runtime.CommitAsync<CommitReceipt>(OperationKind.Batch, mutation, mutation.CommandId, token);
        return new(request with
        {
            Mode = TextIndexMaintenanceMode.Restore
        }, mutation, receipt);
    }
}
