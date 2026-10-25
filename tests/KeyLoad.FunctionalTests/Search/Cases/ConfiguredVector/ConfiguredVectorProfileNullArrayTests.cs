using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.FunctionalTests.Messaging;
using KeyLoad.Server;
using KeyLoad.Storage;
using TUnit.Assertions.Enums;

namespace KeyLoad.FunctionalTests.Search;

internal sealed class ConfiguredVectorProfileNullArrayTests
{
    private const string Definition = "definition";
    private const string Profiles = "vectorProfiles";
    private const string InvalidPayload = "The operation contains invalid protocol JSON.";
    private const string InvalidArguments = "The tool arguments do not match the canonical operation contract.";

    [Test]
    public async Task NullProfileArrayRejectsBeforePersistenceAndMcpThenSameIdConfiguresHealthyResource()
    {
        using var database = new TestDatabase();
        var definition = new ResourceDefinition(ConfiguredVectorProfileFlow.Collection, ResourceKind.Collection,
            database.Partition.TransactionDomainId)
        {
            VectorProfiles = [new(ConfiguredVectorProfileFlow.Field, ConfiguredVectorProfileFlow.Space)]
        };
        var healthy = new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, definition);
        var malformed = JsonNode.Parse(JsonSerializer.Serialize(healthy, JsonDefaults.Options))!;
        malformed[Definition]![Profiles] = null;
        using var document = JsonDocument.Parse(malformed.ToJsonString());
        var id = Guid.NewGuid();
        var complete = QueueWholeFlowStorage.Bytes(database.Store);
        var rejectedPosition = database.Store.Position;
        var applied = database.Database.LastApplied;
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var rejected = Assert.ThrowsExactly<KeyLoadException>(() =>
                database.Submit(OperationKind.ConfigureResource, document.RootElement, id: id));
            await Assert.That(rejected.Code).IsEqualTo(ErrorCode.Validation);
            await Assert.That(rejected.Message).IsEqualTo(InvalidPayload);
        }
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(complete, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(rejectedPosition);
        await Assert.That(database.Database.LastApplied).IsEqualTo(applied);
        var resourceKey = KeySpace.Resource(database.Partition.TenantId, database.Partition.DatabaseId, definition.Name);
        await Assert.That(database.Store.Read(view => view.GetRecord<ResourceDefinition>(resourceKey))).IsNull();
        await Assert.That(database.Store.Read(view => view.ReadOwnedValue(OutcomeStoreOracle.UnknownKey("root", id)))).IsNull();
        var arguments = new Dictionary<string, JsonElement>
        {
            [McpCatalogProtocol.Request] = document.RootElement.Clone(),
            [McpCatalogProtocol.CommandId] = JsonSerializer.SerializeToElement(id, JsonDefaults.Options)
        };
        var officialBoundary = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpArgumentDecoder.HeaderCommand<ConfigureResourceRequest>(arguments, OperationKind.ConfigureResource,
                UnitMcpOptions.CanonicalPayloadBytes));
        await Assert.That(officialBoundary.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(officialBoundary.Message).IsEqualTo(InvalidArguments);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(complete, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(rejectedPosition);
        var healthyId = id;
        var accepted = database.Submit(OperationKind.ConfigureResource, healthy, id: healthyId);
        await Assert.That(OrleansSerialization.Serialize(accepted.Get<ResourceDefinition>()).SequenceEqual(
            OrleansSerialization.Serialize(definition))).IsTrue();
        var committed = QueueWholeFlowStorage.Bytes(database.Store);
        var committedPosition = database.Store.Position;
        await Assert.That(OrleansSerialization.Serialize(database.Store.Read(view => view.GetRecord<ResourceDefinition>(resourceKey))!)
            .SequenceEqual(OrleansSerialization.Serialize(definition))).IsTrue();
        var replay = database.Submit(OperationKind.ConfigureResource, healthy, id: healthyId);
        await Assert.That(OrleansSerialization.Serialize(replay).SequenceEqual(OrleansSerialization.Serialize(accepted))).IsTrue();
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(committed, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(committedPosition);
    }
}
