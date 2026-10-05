using KeyLoad.Core;
using KeyLoad.Storage;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

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
    public async Task AcRoc003NullResourceProtocolPersistsValidationWithoutPartialCatalogEffects(string payload)
    {
        using var database = new TestDatabase();
        var operation = new ReplicatedOperation(Guid.NewGuid(), OperationKind.ConfigureResource, RootPrincipal,
            TimeProvider.System.GetUtcNow(), payload);

        var failure = database.Database.Apply(operation);

        await Assert.That(failure.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(OutcomeStoreOracle.Read(database.Store, operation)).IsEqualTo(failure);
        var absent = database.Store.Read(view => view.GetRecord<ResourceDefinition>(
            KeySpace.Resource(database.Partition.TenantId, database.Partition.DatabaseId, CollectionName)));
        await Assert.That(absent).IsNull();
        await Assert.That(database.Configure(CollectionName, ResourceKind.Collection).Name).IsEqualTo(CollectionName);
    }

    [Test]
    public async Task AcRoc003InvalidResourceFailureReplayRetainsItsOriginalOutcomeAndFingerprint()
    {
        using var database = new TestDatabase();
        var operation = new ReplicatedOperation(Guid.NewGuid(), OperationKind.ConfigureResource, RootPrincipal,
            TimeProvider.System.GetUtcNow(), NullDefinition);
        var original = database.Database.Apply(operation, replicationIndex: 7);
        var replay = database.Database.Apply(operation, replicationIndex: 8);

        await Assert.That(replay).IsEqualTo(original);
        await Assert.That(replay.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(database.Database.LastApplied).IsEqualTo(8);
        await Assert.That(database.Database.ResolveOutcome(operation)).IsEqualTo(original);
    }
}
