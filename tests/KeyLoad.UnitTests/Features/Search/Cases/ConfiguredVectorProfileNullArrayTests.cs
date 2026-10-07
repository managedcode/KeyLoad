using System.Text.Json;
using System.Text.Json.Nodes;
using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.Storage;
using KeyLoad.UnitTests.Features.Messaging;
using TUnit.Assertions.Enums;

namespace KeyLoad.UnitTests.Features.Search;

internal sealed class ConfiguredVectorProfileNullArrayTests
{
    private const string Definition = "definition";
    private const string Profiles = "vectorProfiles";
    private const string InvalidPayload = "The operation contains invalid protocol JSON.";
    private const string InvalidArguments = "The tool arguments do not match the canonical operation contract.";
    private const int CommitAdvance = 1;

    [Test]
    public async Task NullProfileArrayKeepsFailedOutcomeAndMcpRejectsBeforeHealthyResourceConfiguration()
    {
        using var database = new TestDatabase();
        var definition = new ResourceDefinition(ConfiguredVectorProfileFlow.Collection, ResourceKind.Collection,
            database.Partition.TransactionDomainId)
        { VectorProfiles = [new(ConfiguredVectorProfileFlow.Field, ConfiguredVectorProfileFlow.Space)] };
        var healthy = new ConfigureResourceRequest(database.Partition.TenantId, database.Partition.DatabaseId, definition);
        var malformed = JsonNode.Parse(JsonSerializer.Serialize(healthy, JsonDefaults.Options))!;
        malformed[Definition]![Profiles] = null;
        using var document = JsonDocument.Parse(malformed.ToJsonString());
        var id = Guid.NewGuid();
        var position = database.Store.Position;
        var rejected = database.Submit(OperationKind.ConfigureResource, document.RootElement, id: id);
        await Assert.That(rejected.Error).IsEqualTo(ErrorCode.Validation);
        await Assert.That(rejected.SafeDetail).IsEqualTo(InvalidPayload);
        await Assert.That(rejected.Json).IsNull();
        await Assert.That(database.Store.Position).IsEqualTo(position + CommitAdvance);
        var resourceKey = KeySpace.Resource(database.Partition.TenantId, database.Partition.DatabaseId, definition.Name);
        await Assert.That(database.Store.Read(view => view.GetRecord<ResourceDefinition>(resourceKey))).IsNull();
        var stored = database.Store.Read(view => view.GetRecord<StoredOutcome>(OutcomeStoreOracle.UnknownKey("root", id)))!;
        await Assert.That(NativeSerialization.Serialize(stored.Result).SequenceEqual(NativeSerialization.Serialize(rejected))).IsTrue();
        var complete = QueueWholeFlowStorage.Bytes(database.Store);
        var rejectedPosition = database.Store.Position;
        var rejectedReplay = database.Submit(OperationKind.ConfigureResource, document.RootElement, id: id);
        await Assert.That(NativeSerialization.Serialize(rejectedReplay).SequenceEqual(NativeSerialization.Serialize(rejected))).IsTrue();
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(complete, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(rejectedPosition);
        var arguments = new Dictionary<string, JsonElement>
        {
            [McpCatalogProtocol.Request] = document.RootElement.Clone(),
            [McpCatalogProtocol.CommandId] = JsonSerializer.SerializeToElement(id, JsonDefaults.Options)
        };
        var officialBoundary = Assert.ThrowsExactly<KeyLoadException>(() =>
            McpArgumentDecoder.HeaderCommand<ConfigureResourceRequest>(arguments, OperationKind.ConfigureResource,
                database.Database.Limits.MaxBatchBytes));
        await Assert.That(officialBoundary.Code).IsEqualTo(ErrorCode.Validation);
        await Assert.That(officialBoundary.Message).IsEqualTo(InvalidArguments);
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(complete, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(rejectedPosition);
        var healthyId = Guid.NewGuid();
        var accepted = database.Submit(OperationKind.ConfigureResource, healthy, id: healthyId);
        await Assert.That(NativeSerialization.Serialize(accepted.Get<ResourceDefinition>()).SequenceEqual(
            NativeSerialization.Serialize(definition))).IsTrue();
        var committed = QueueWholeFlowStorage.Bytes(database.Store);
        var committedPosition = database.Store.Position;
        await Assert.That(NativeSerialization.Serialize(database.Store.Read(view => view.GetRecord<ResourceDefinition>(resourceKey))!)
            .SequenceEqual(NativeSerialization.Serialize(definition))).IsTrue();
        var replay = database.Submit(OperationKind.ConfigureResource, healthy, id: healthyId);
        await Assert.That(NativeSerialization.Serialize(replay).SequenceEqual(NativeSerialization.Serialize(accepted))).IsTrue();
        await Assert.That(QueueWholeFlowStorage.Bytes(database.Store)).IsEquivalentTo(committed, CollectionOrdering.Matching);
        await Assert.That(database.Store.Position).IsEqualTo(committedPosition);
    }
}
