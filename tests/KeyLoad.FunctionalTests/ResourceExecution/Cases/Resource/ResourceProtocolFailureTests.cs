using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Storage;

namespace KeyLoad.FunctionalTests.ResourceExecution;

internal sealed class ResourceProtocolFailureTests
{
    private const string RootPrincipal = "root";
    private const string NullDefinition = "{\"tenantId\":\"tenant\",\"databaseId\":\"database\",\"definition\":null}";
    private const string CollectionName = "protocol-guard";
    private const string NullQueuePolicy = "{\"tenantId\":\"tenant\",\"databaseId\":\"database\",\"definition\":{\"name\":\"protocol-guard\",\"kind\":\"collection\",\"transactionDomainId\":\"orders\",\"queuePolicy\":null}}";
    private const string NullEventRetention = "{\"tenantId\":\"tenant\",\"databaseId\":\"database\",\"definition\":{\"name\":\"protocol-guard\",\"kind\":\"collection\",\"transactionDomainId\":\"orders\",\"eventRetention\":null}}";

    [Test]
    [Arguments(NullDefinition)]
    [Arguments(NullQueuePolicy)]
    [Arguments(NullEventRetention)]
    public async Task AcRoc003NullResourceProtocolIsRejectedBeforeCatalogEffects(string payload)
    {
        using var database = new TestDatabase();
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var cut = database.ReadCanonicalCut();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var failure = Assert.ThrowsExactly<KeyLoadException>(() => database.Database.CreateCommand(
                OperationKind.ConfigureResource, Guid.NewGuid(), RootPrincipal, TimeProvider.System.GetUtcNow(), payload));
            await Assert.That(failure.Code).IsEqualTo(ErrorCode.Validation);
        }
        await Assert.That(database.ReadCanonicalCut()).IsEqualTo(cut);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store).SequenceEqual(image)).IsTrue();
        var absent = database.Store.Read(view => view.GetRecord<ResourceDefinition>(
            KeySpace.Resource(database.Partition.TenantId, database.Partition.DatabaseId, CollectionName)));
        await Assert.That(absent).IsNull();
        await Assert.That(database.Configure(CollectionName, ResourceKind.Collection).Name).IsEqualTo(CollectionName);
    }

    [Test]
    public async Task AcRoc003InvalidResourceFailureReplayRetainsItsOriginalOutcomeAndFingerprint()
    {
        using var database = new TestDatabase();
        var operation = database.Database.CreateCommand(OperationKind.ConfigureResource, Guid.NewGuid(), RootPrincipal, TimeProvider.System.GetUtcNow(),
            "{\"tenantId\":\"tenant\",\"databaseId\":\"database\",\"definition\":{\"name\":\"\",\"kind\":\"collection\",\"transactionDomainId\":\"orders\"}}");
        var applied = database.Database.LastApplied;
        var original = database.Database.Apply(operation);
        var image = QueueWholeFlowStorage.Bytes(database.Store);
        var position = database.Store.Position;
        var replay = database.Database.Apply(operation);

        await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(OrleansSerialization.Serialize(original))).IsTrue();
        await Assert.That(Errors.Code(replay.Problem).Value).IsEqualTo(ErrorCode.Validation);
        await Assert.That(database.Database.LastApplied).IsEqualTo(applied + 1);
        await Assert.That(database.Store.Position).IsEqualTo(position);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store).SequenceEqual(image)).IsTrue();
        await Assert.That(OrleansSerialization.Serialize(database.Database.ResolveOutcome(operation))
            .SequenceEqual(OrleansSerialization.Serialize(original))).IsTrue();
    }
}
