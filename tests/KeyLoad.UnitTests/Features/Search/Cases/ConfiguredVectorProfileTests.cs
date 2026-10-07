using System.Collections.Immutable;
using System.Text.Json;
using KeyLoad.Storage;
using KeyLoad.Storage.ZoneTree;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class ConfiguredVectorProfileTests
{
    private const string Root = "root";
    private const string DatabaseScope = "database";
    private const string Reader = "configured-reader";
    private const string Denied = "The principal cannot perform this operation in this scope.";
    private const string PutDocumentKind = "putDocument";
    private const string PutVectorKind = "putVector";
    private const string ProjectionKind = "applyVectorProjection";
    private const long HealthyRevision = 2;
    private const int MutationCount = 2;
    private const string ChangedModel = "changed-authority";
    private const string UnsupportedDefinition = "The requested resource definition change is unsupported.";

    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task ConfiguredModelMismatchRollsBackDirectAndLineageEffectsThenReplaysAndReopensHealthy(bool projection)
    {
        using var database = new TestDatabase();
        ConfiguredVectorProfileFlow.Seed(database);
        var command = ConfiguredVectorProfileFlow.Command(database, projection, wrongModel: true);
        await ConfiguredVectorProfileFlow.RejectAsync(database, command);
        var healthy = ConfiguredVectorProfileFlow.Command(database, projection, wrongModel: false);
        var receipt = database.Submit(OperationKind.Batch, healthy, id: healthy.CommandId).Get<CommitReceipt>();
        await Assert.That(receipt.CommandId).IsEqualTo(healthy.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(database.Partition.AtomicPartitionId);
        await Assert.That(receipt.Mutations.Length).IsEqualTo(MutationCount);
        var expected = new MutationReceipt[]
        {
            new(PutDocumentKind, ConfiguredVectorProfileFlow.Collection, ConfiguredVectorProfileFlow.Target, HealthyRevision),
            new(projection ? ProjectionKind : PutVectorKind, ConfiguredVectorProfileFlow.Collection,
                ConfiguredVectorProfileFlow.Target, HealthyRevision)
        };
        await Assert.That(JsonDefaults.Serialize(receipt.Mutations).SequenceEqual(
            JsonDefaults.Serialize(expected.ToImmutableArray()))).IsTrue();
        for (var index = 0; index < expected.Length; index++)
        {
            await Assert.That(NativeSerialization.Serialize(receipt.Mutations[index]).SequenceEqual(
                NativeSerialization.Serialize(expected[index]))).IsTrue();
        }
        var token = TestContext.Current!.Execution.CancellationToken;
        await ConfiguredVectorProfileFlow.HealthyAsync(database.Database, database.Partition, token);
        var complete = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var replay = database.Submit(OperationKind.Batch, healthy, id: healthy.CommandId).Get<CommitReceipt>();
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(complete, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        database.Store.Dispose();
        using var reopened = new ZoneTreeStore(new(database.Directory), UnitExecutionOptions.StorageExecution(),
            UnitExecutionOptions.PointCacheExecution());
        var owner = QueueWholeFlowStorage.Open(reopened);
        await ConfiguredVectorProfileFlow.HealthyAsync(owner, database.Partition, token);
        await Assert.That(QueueWholeFlowStorage.Bytes(reopened)).IsEquivalentTo(complete, CollectionOrdering.Matching);
        await Assert.That(reopened.Position).IsEqualTo(position);
        var restoredReceipt = owner.ApplyEmbedded(new(healthy.CommandId, OperationKind.Batch, Root, default,
            JsonSerializer.Serialize(healthy, JsonDefaults.Options)), cancellationToken: default).Get<CommitReceipt>();
        await Assert.That(NativeSerialization.Serialize(restoredReceipt).SequenceEqual(NativeSerialization.Serialize(receipt))).IsTrue();
        await Assert.That(QueueWholeFlowStorage.Bytes(reopened)).IsEquivalentTo(complete, CollectionOrdering.Matching);
        await Assert.That(reopened.Position).IsEqualTo(position);
    }

    [Test]
    public async Task PersistedProfileCannotBeChangedByAdministratorAndOriginalModelRemainsUsable()
    {
        using var database = new TestDatabase();
        ConfiguredVectorProfileFlow.Seed(database);
        var key = KeyLoad.Core.KeySpace.Resource(database.Partition.TenantId, database.Partition.DatabaseId,
            ConfiguredVectorProfileFlow.Collection);
        var original = database.Store.Read(view => view.GetRecord<ResourceDefinition>(key))!;
        var rejected = database.Submit(OperationKind.ConfigureResource, new ConfigureResourceRequest(database.Partition.TenantId,
            database.Partition.DatabaseId, original with
            { VectorProfiles = [new(ConfiguredVectorProfileFlow.Field, ConfiguredVectorProfileFlow.Space with { Model = ChangedModel })] }));
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.UnsupportedCapability);
        await Assert.That(rejected.SafeDetail).IsEqualTo(UnsupportedDefinition);
        var restored = database.Store.Read(view => view.GetRecord<ResourceDefinition>(key))!;
        await Assert.That(NativeSerialization.Serialize(restored).SequenceEqual(NativeSerialization.Serialize(original))).IsTrue();
        var healthy = ConfiguredVectorProfileFlow.Command(database, projection: false, wrongModel: false);
        database.Submit(OperationKind.Batch, healthy, principal: Root, id: healthy.CommandId).Get<CommitReceipt>();
        await ConfiguredVectorProfileFlow.HealthyAsync(database.Database, database.Partition, TestContext.Current!.Execution.CancellationToken);
    }
    [Test]
    public async Task CurrentDeniedPrincipalCannotProbeConfiguredModelAndHealthyAuthorizedWriteFollows()
    {
        using var database = new TestDatabase();
        ConfiguredVectorProfileFlow.Seed(database);
        database.Submit(OperationKind.ConfigurePrincipal, new ConfigurePrincipalRequest(new(Reader,
            database.Partition.TenantId, [new(DatabaseScope, ConfiguredVectorProfileFlow.Collection,
                Capability.DocumentsRead | Capability.Query)], []))).Get<PrincipalRecord>();
        var command = ConfiguredVectorProfileFlow.Command(database, projection: false, wrongModel: true);
        var model = ConfiguredVectorProfileFlow.ModelBytes(database, command, Reader);
        var error = database.Submit(OperationKind.Batch, command, principal: Reader, id: command.CommandId);
        await Assert.That(error.Error).IsEqualTo(ErrorCode.PermissionDenied);
        await Assert.That(error.SafeDetail).IsEqualTo(Denied);
        await Assert.That(error.Json).IsNull();
        await Assert.That(ConfiguredVectorProfileFlow.ModelBytes(database, command, Reader)).IsEquivalentTo(model, CollectionOrdering.Matching);
        var healthy = ConfiguredVectorProfileFlow.Command(database, projection: false, wrongModel: false);
        database.Submit(OperationKind.Batch, healthy, id: healthy.CommandId).Get<CommitReceipt>();
        await ConfiguredVectorProfileFlow.HealthyAsync(database.Database, database.Partition, TestContext.Current!.Execution.CancellationToken);
    }

}
