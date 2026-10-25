using System.Text.Json;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.OrleansExecution;
using KeyLoad.ClusterTests.QueryExecution;
using KeyLoad.Core;

namespace KeyLoad.ClusterTests.Authorization;

internal static class ModelRootClusterSeed
{
    internal static async Task<ModelRootClusterState> RunAsync(RequestCqrsClusterCallers root, PartitionRef partition,
        bool queue, bool headerRoot, long eventSequence, CancellationToken token)
    {
        var suffix = Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var resource = "root-" + suffix;
        var reader = "reader-" + suffix;
        var key = reader + ".root-policy-credential-32-characters";
        var rootPolicy = new SensitiveFieldPolicy("", "root-classification", ModelRootClusterState.ReadGrant, ModelRootClusterState.UseGrant);
        var literal = new SensitiveFieldPolicy("/", "empty-child", "literal.read", "literal.use");
        var definition = new ResourceDefinition(resource, queue ? ResourceKind.WorkQueue : ResourceKind.StreamSet,
            partition.TransactionDomainId)
        {
            FieldPolicies = [headerRoot ? literal : rootPolicy],
            HeaderPolicies = [headerRoot ? rootPolicy : literal]
        };
        await SqlClusterProtocol.EqualAsync(definition, await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigureResourceAsync(
            Guid.NewGuid(), new(partition.TenantId, partition.DatabaseId, definition), token)));
        Mutation mutation = queue ? new EnqueueMessage(resource, ModelRootClusterState.Item, ModelRootClusterState.Body, ModelRootClusterState.Body)
            : new AppendEvents(resource, ModelRootClusterState.Item,
                [new(ModelRootClusterState.Item, ModelRootClusterState.Type, ModelRootClusterState.Body, ModelRootClusterState.Body)], ExpectedStreamRevision.NoStream);
        var seed = new CommandRequest(Guid.NewGuid(), partition, [mutation]);
        var receipt = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.CommitAsync(seed, token));
        await Assert.That(receipt.CommandId).IsEqualTo(seed.CommandId);
        await Assert.That(receipt.Token.AtomicPartitionId).IsEqualTo(partition.AtomicPartitionId);
        await Assert.That(receipt.Token.Position).IsGreaterThan(0L);
        await Assert.That(receipt.Durability).IsEqualTo(DurabilityProfile.ProcessDurable);
        await Assert.That(receipt.PersistedCopies).IsEqualTo(3);
        await SqlClusterProtocol.EqualAsync<MutationReceipt[]>([new(queue ? "enqueue" : "appendEvents", resource, ModelRootClusterState.Item, 1L)], receipt.Mutations.ToArray());
        var state = new ModelRootClusterState(partition, queue, headerRoot, resource, reader, key, seed, receipt, null!);
        await ConfigureAsync(root, state, 1L, false, false, token);
        await McpCallerAssertions.SdkStatusAsync(await root.Sdk.ConfigureApiKeyAsync(Guid.NewGuid(), AuthorizationExecution.Credential(reader, reader, key), token));
        var raw = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.QueryAstAsync(state.Ast(), token));
        await Assert.That(raw.Rows.Length).IsEqualTo(1);
        await Assert.That(raw.Cursor).IsNull();
        await Assert.That(raw.AccessPath).IsEqualTo(state.AccessPath);
        var row = raw.Rows.Single();
        await OriginalRowAsync(row, queue, eventSequence);
        return state with
        {
            Original = row
        };
    }

    private static async Task OriginalRowAsync(QueryRow row, bool queue, long eventSequence)
    {
        await Assert.That(row.EntityId).IsEqualTo(ModelRootClusterState.Item);
        await Assert.That(row.Revision).IsEqualTo(1L);
        await Assert.That(row.Redacted).IsFalse();
        using var json = JsonDocument.Parse(row.Json);
        using var body = JsonDocument.Parse(ModelRootClusterState.Body);
        await Assert.That(JsonElement.DeepEquals(json.RootElement.GetProperty(ModelRootClusterState.PayloadField), body.RootElement)).IsTrue();
        await Assert.That(JsonElement.DeepEquals(json.RootElement.GetProperty(ModelRootClusterState.HeadersField), body.RootElement)).IsTrue();
        var names = json.RootElement.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal).ToArray();
        var expectedNames = queue ? new[] { ModelRootClusterState.IdField, ModelRootClusterState.StateField, ModelRootClusterState.AttemptsField, ModelRootClusterState.StateVersionField, ModelRootClusterState.NotBeforeField, ModelRootClusterState.ExpiresAtField, ModelRootClusterState.PayloadField, ModelRootClusterState.HeadersField }
            : new[] { ModelRootClusterState.EventIdField, ModelRootClusterState.EventTypeField, ModelRootClusterState.SchemaVersionField, ModelRootClusterState.RevisionField, ModelRootClusterState.EventSequenceField, ModelRootClusterState.RecordedAtField, ModelRootClusterState.PayloadField, ModelRootClusterState.HeadersField };
        await Assert.That(names.SequenceEqual(expectedNames.Order(StringComparer.Ordinal), StringComparer.Ordinal)).IsTrue();
        await Assert.That(json.RootElement.GetProperty(queue ? ModelRootClusterState.IdField : ModelRootClusterState.EventIdField).GetString()).IsEqualTo(ModelRootClusterState.Item);
        if (queue)
        {
            await Assert.That(json.RootElement.GetProperty(ModelRootClusterState.StateField).GetString()).IsEqualTo("Ready");
            await Assert.That(json.RootElement.GetProperty(ModelRootClusterState.AttemptsField).GetInt32()).IsEqualTo(0);
            await Assert.That(json.RootElement.GetProperty(ModelRootClusterState.StateVersionField).GetInt64()).IsEqualTo(1L);
            await Assert.That(json.RootElement.GetProperty(ModelRootClusterState.NotBeforeField).ValueKind).IsEqualTo(JsonValueKind.Null);
            await Assert.That(json.RootElement.GetProperty(ModelRootClusterState.ExpiresAtField).ValueKind).IsEqualTo(JsonValueKind.Null);
        }
        else
        {
            await Assert.That(json.RootElement.GetProperty(ModelRootClusterState.EventTypeField).GetString()).IsEqualTo(ModelRootClusterState.Type);
            await Assert.That(json.RootElement.GetProperty(ModelRootClusterState.SchemaVersionField).GetInt32()).IsEqualTo(1);
            await Assert.That(json.RootElement.GetProperty(ModelRootClusterState.RevisionField).GetInt64()).IsEqualTo(1L);
            await Assert.That(json.RootElement.GetProperty(ModelRootClusterState.EventSequenceField).GetInt64()).IsEqualTo(eventSequence);
            await Assert.That(json.RootElement.GetProperty(ModelRootClusterState.RecordedAtField).GetDateTimeOffset()).IsGreaterThan(DateTimeOffset.UnixEpoch);
        }
    }

    internal static async Task ConfigureAsync(RequestCqrsClusterCallers root, ModelRootClusterState state,
        long epoch, bool use, bool read, CancellationToken token)
    {
        var expected = state.Principal(epoch, use, read);
        var actual = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.ConfigurePrincipalAsync(Guid.NewGuid(), expected, token));
        await SqlClusterProtocol.EqualAsync(expected, actual);
    }
}
