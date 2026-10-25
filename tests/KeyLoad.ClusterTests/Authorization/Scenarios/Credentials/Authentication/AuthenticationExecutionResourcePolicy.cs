using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.DocumentStorage;
using KeyLoad.ClusterTests.QueryExecution;
using ModelContextProtocol.Client;

namespace KeyLoad.ClusterTests.Authorization;

internal static class AuthenticationExecutionResourcePolicy
{
    private const long InitialVersion = 1;
    private const long RestrictedVersion = 2;
    private const long RepairedVersion = 3;
    private const string PublicField = "public";
    private const string PublicValue = "Привіт é 😀 \"quoted\"";

    internal static async Task RunAsync(KeyLoadClient root, KeyLoadClient reader, McpClient mcp,
        AuthenticationExecutionHttpHandler http, AuthenticationExecutionCollector collector,
        AuthenticationExecutionState state, CancellationToken token)
    {
        var partition = state.Document.Partition;
        var definition = new ResourceDefinition(McpDocumentProtocol.Collection, ResourceKind.Collection, partition.TransactionDomainId);
        var catalog = await McpCallerAssertions.SdkSuccessAsync(await root.ListResourcesAsync(
            new(partition.TenantId, partition.DatabaseId), token));
        var current = catalog.Items.Single(item => item.Name == definition.Name);
        await Assert.That(current.SchemaVersion).IsEqualTo(InitialVersion);
        var protectedDefinition = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, definition with
            {
                SchemaVersion = checked(current.SchemaVersion + 1),
                FieldPolicies = [new(McpDocumentProtocol.ProtectedPath, McpDocumentProtocol.ProtectedClassification)]
            })
            {
                ExpectedSchemaVersion = current.SchemaVersion
            }, token));
        await SqlClusterProtocol.EqualAsync(definition with
        {
            SchemaVersion = RestrictedVersion,
            FieldPolicies = [new(McpDocumentProtocol.ProtectedPath, McpDocumentProtocol.ProtectedClassification)]
        }, protectedDefinition);
        var before = await AuthenticationExecutionValues.ImageAsync(root, state, token);
        var position = collector.Position;
        var sent = http.Requests;
        await RedactedAsync(await PublicReadAssertions.DocumentAsync(await reader.GetAsync(state.Document.Reference, token)), state);
        await AuthenticationExecutionAssertions.WindowAsync(collector, position, http.Requests - sent, state.Reader.Id);
        position = collector.Position;
        sent = http.Requests;
        var official = await PublicReadAssertions.RequiredDocumentAsync(await AuthenticationExecutionClients.ReadAsync(mcp, state, token));
        await RedactedAsync(official.Value, state);
        await AuthenticationExecutionAssertions.WindowAsync(collector, position, http.Requests - sent, state.Reader.Id);
        await Assert.That(await AuthenticationExecutionValues.ImageAsync(root, state, token)).IsEqualTo(before);
        var repaired = await McpCallerAssertions.SdkSuccessAsync(await root.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, definition with
            {
                SchemaVersion = checked(protectedDefinition.SchemaVersion + 1)
            })
            {
                ExpectedSchemaVersion = protectedDefinition.SchemaVersion
            }, token));
        await SqlClusterProtocol.EqualAsync(definition with
        {
            SchemaVersion = RepairedVersion
        }, repaired);
        await AuthenticationExecutionValues.HealthyAsync(reader, mcp, http, collector, state, state.Expected, token);
    }

    private static async Task RedactedAsync(DocumentResult? actual, AuthenticationExecutionState state)
    {
        await Assert.That(actual).IsNotNull();
        var expected = state.Expected with
        {
            Redacted = true,
            RedactedFields = [McpDocumentProtocol.ProtectedPath]
        };
        await SqlClusterProtocol.EqualAsync(expected.Reference, actual!.Reference);
        await Assert.That(actual.Revision).IsEqualTo(expected.Revision);
        await Assert.That(actual.Redacted).IsEqualTo(expected.Redacted);
        await Assert.That(actual.RedactedFields.SequenceEqual(expected.RedactedFields)).IsTrue();
        using var json = JsonDocument.Parse(actual.Json);
        var literal = JsonSerializer.SerializeToElement(new Dictionary<string, string> { [PublicField] = PublicValue }, JsonDefaults.Options);
        await Assert.That(JsonElement.DeepEquals(json.RootElement, literal)).IsTrue();
    }
}
