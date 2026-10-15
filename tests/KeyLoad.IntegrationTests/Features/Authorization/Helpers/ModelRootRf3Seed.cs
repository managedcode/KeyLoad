using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.Core;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;

namespace KeyLoad.IntegrationTests.Features.Authorization;

internal static class ModelRootRf3Seed
{
    internal static async Task<ModelRootRf3State> RunAsync(RequestCqrsRf3Callers root, PartitionRef partition,
        bool queue, bool headerRoot, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var resource = "root-" + suffix;
        var reader = "reader-" + suffix;
        var key = reader + ".root-policy-credential-32-characters";
        var rootPolicy = new SensitiveFieldPolicy("", "root-classification", ModelRootRf3State.ReadGrant, ModelRootRf3State.UseGrant);
        var literal = new SensitiveFieldPolicy("/", "empty-child", "literal.read", "literal.use");
        var definition = new ResourceDefinition(resource, queue ? ResourceKind.WorkQueue : ResourceKind.StreamSet,
            partition.TransactionDomainId)
        {
            FieldPolicies = [headerRoot ? literal : rootPolicy],
            HeaderPolicies = [headerRoot ? rootPolicy : literal]
        };
        await SqlRf3Protocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(
            Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, definition), token)));
        Mutation mutation = queue ? new EnqueueMessage(resource, ModelRootRf3State.Item, ModelRootRf3State.Body, ModelRootRf3State.Body)
            : new AppendEvents(resource, ModelRootRf3State.Item,
                [new(ModelRootRf3State.Item, ModelRootRf3State.Type, ModelRootRf3State.Body, ModelRootRf3State.Body)], ExpectedStreamRevision.NoStream);
        var seed = new CommandRequest(Guid.NewGuid(), partition, [mutation]);
        var placement = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ReadAtomicPartitionPlacementAsync(new(1, partition), token));
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(seed, token));
        await Assert.That(receipt.CommandId).IsEqualTo(seed.CommandId);
        await Assert.That(receipt.Token.Incarnation).IsEqualTo(placement.Incarnation);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        await Assert.That(receipt.Token.OwnershipEpoch).IsEqualTo(placement.PlacementEpoch);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await SqlRf3Protocol.EqualAsync<MutationReceipt[]>([new(queue ? "enqueue" : "appendEvents", resource, ModelRootRf3State.Item, 1L)], receipt.Mutations.ToArray());
        var state = new ModelRootRf3State(partition, queue, headerRoot, resource, reader, key, seed, receipt, null!);
        await ConfigureAsync(root, state, 1L, false, false, token);
        await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(), DatabaseEngine.Credential(reader, reader, key), token));
        var raw = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.QueryAstAsync(state.Ast(), token));
        await Assert.That(raw.Rows.Length).IsEqualTo(1);
        await Assert.That(raw.Cursor).IsNull();
        await Assert.That(raw.AccessPath).IsEqualTo(state.AccessPath);
        var row = raw.Rows.Single();
        await OriginalRowAsync(row, queue);
        return state with
        {
            Original = row
        };
    }

    private static async Task OriginalRowAsync(QueryRow row, bool queue)
    {
        await Assert.That(row.EntityId).IsEqualTo(ModelRootRf3State.Item);
        await Assert.That(row.Revision).IsEqualTo(1L);
        await Assert.That(row.Redacted).IsFalse();
        using var json = JsonDocument.Parse(row.Json);
        using var body = JsonDocument.Parse(ModelRootRf3State.Body);
        await Assert.That(JsonElement.DeepEquals(json.RootElement.GetProperty(ModelRootRf3State.PayloadField), body.RootElement)).IsTrue();
        await Assert.That(JsonElement.DeepEquals(json.RootElement.GetProperty(ModelRootRf3State.HeadersField), body.RootElement)).IsTrue();
        var names = json.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
        var expectedNames = queue ? new[] { ModelRootRf3State.IdField, ModelRootRf3State.StateField, ModelRootRf3State.AttemptsField, ModelRootRf3State.StateVersionField, ModelRootRf3State.NotBeforeField, ModelRootRf3State.ExpiresAtField, ModelRootRf3State.PayloadField, ModelRootRf3State.HeadersField }
            : new[] { ModelRootRf3State.EventIdField, ModelRootRf3State.EventTypeField, ModelRootRf3State.SchemaVersionField, ModelRootRf3State.RevisionField, ModelRootRf3State.EventSequenceField, ModelRootRf3State.RecordedAtField, ModelRootRf3State.PayloadField, ModelRootRf3State.HeadersField };
        await Assert.That(names.SequenceEqual(expectedNames.Order(StringComparer.Ordinal), StringComparer.Ordinal)).IsTrue();
        await Assert.That(json.RootElement.GetProperty(queue ? ModelRootRf3State.IdField : ModelRootRf3State.EventIdField).GetString()).IsEqualTo(ModelRootRf3State.Item);
        if (queue)
        {
            await Assert.That(json.RootElement.GetProperty(ModelRootRf3State.StateField).GetString()).IsEqualTo("Ready");
            await Assert.That(json.RootElement.GetProperty(ModelRootRf3State.AttemptsField).GetInt32()).IsEqualTo(0);
            await Assert.That(json.RootElement.GetProperty(ModelRootRf3State.StateVersionField).GetInt64()).IsEqualTo(1L);
            await Assert.That(json.RootElement.GetProperty(ModelRootRf3State.NotBeforeField).ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(json.RootElement.GetProperty(ModelRootRf3State.ExpiresAtField).ValueKind).IsEqualTo(JsonValueKind.Null);
        }
        else
        {
            await Assert.That(json.RootElement.GetProperty(ModelRootRf3State.EventTypeField).GetString()).IsEqualTo(ModelRootRf3State.Type);
            await Assert.That(json.RootElement.GetProperty(ModelRootRf3State.SchemaVersionField).GetInt32()).IsEqualTo(1);
            await Assert.That(json.RootElement.GetProperty(ModelRootRf3State.RevisionField).GetInt64()).IsEqualTo(1L);
            await Assert.That(json.RootElement.GetProperty(ModelRootRf3State.EventSequenceField).GetInt64()).IsEqualTo(1L);
            await Assert.That(json.RootElement.GetProperty(ModelRootRf3State.RecordedAtField).GetDateTimeOffset()).IsGreaterThan(DateTimeOffset.UnixEpoch);
        }
    }

    internal static async Task ConfigureAsync(RequestCqrsRf3Callers root, ModelRootRf3State state,
        long epoch, bool use, bool read, CancellationToken token)
    {
        var expected = state.Principal(epoch, use, read);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await SqlRf3Protocol.EqualAsync(expected, actual);
    }
}
