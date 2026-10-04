using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Authorization;

/// <summary>Checks policy outcomes through independent public SDK and official MCP callers.</summary>
internal static class ResourcePolicyUpdateRf3Assertions
{
    private const string FirstNode = McpCallerProtocol.Node1;
    private const string SecondNode = McpCallerProtocol.Node2;
    private const string ThirdNode = McpCallerProtocol.Node3;

    internal static async Task AssertPolicyDeniedAsync(ClusterFixture fixture,
        ResourcePolicyUpdateRf3Scenario scenario, McpPersistedIdentity reader, McpOfficialClient mcp,
        CancellationToken cancellationToken)
    {
        foreach (var node in new[] { FirstNode, SecondNode, ThirdNode })
        { await AssertSdkDeniedAsync(fixture, scenario, reader, node, cancellationToken); }
        await AssertMcpDeniedAsync(mcp, scenario, cancellationToken);
    }

    private static async Task AssertSdkDeniedAsync(ClusterFixture fixture,
        ResourcePolicyUpdateRf3Scenario scenario, McpPersistedIdentity reader, string node,
        CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, node);
        var sdk = new KeyLoadClient(http, reader.Secret);
        var document = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(scenario.Reference, cancellationToken));
        await Assert.That(document!.Redacted).IsTrue();
        await Assert.That(document.Revision).IsEqualTo(scenario.DocumentRevision);
        await Assert.That(document.RedactedFields).Contains(ResourcePolicyUpdateRf3Protocol.SecretPath);
        await Assert.That(document.Json.Contains(ResourcePolicyUpdateRf3Protocol.SecretValue, StringComparison.Ordinal)).IsFalse();
        var denied = await sdk.QueryAstAsync(ResourcePolicyUpdateRf3Scenario.SecretQuery(scenario.Partition), cancellationToken);
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
    }

    private static async Task AssertMcpDeniedAsync(McpOfficialClient mcp,
        ResourcePolicyUpdateRf3Scenario scenario, CancellationToken cancellationToken)
    {
        var document = await McpCallerAssertions.SuccessAsync<DocumentResult>(await mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(scenario.Reference), cancellationToken));
        await Assert.That(document.Value.Redacted).IsTrue();
        await Assert.That(document.Value.Revision).IsEqualTo(scenario.DocumentRevision);
        await Assert.That(document.Value.RedactedFields).Contains(ResourcePolicyUpdateRf3Protocol.SecretPath);
        await Assert.That(document.Value.Json.Contains(ResourcePolicyUpdateRf3Protocol.SecretValue,
            StringComparison.Ordinal)).IsFalse();
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.QueryAst,
            ResourcePolicyUpdateRf3Scenario.SecretQuery(scenario.Partition), cancellationToken),
            ErrorCode.PermissionDenied, dispatched: true);
    }

    internal static async Task AssertPolicyAllowedAsyncFromInitialAsync(ClusterFixture fixture,
        McpOfficialClient readerMcp, ResourcePolicyUpdateRf3Scenario scenario, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var reader = new KeyLoadClient(http, scenario.Reader.Secret);
        var document = await McpCallerAssertions.SdkSuccessAsync(await reader.GetAsync(scenario.Reference, cancellationToken));
        await Assert.That(document!.Redacted).IsFalse();
        await Assert.That(document.Revision).IsEqualTo(scenario.DocumentRevision);
        var mcpDocument = await McpCallerAssertions.SuccessAsync<DocumentResult>(await readerMcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(scenario.Reference), cancellationToken));
        await Assert.That(mcpDocument.Value.Redacted).IsFalse();
        await Assert.That(mcpDocument.Value.Revision).IsEqualTo(scenario.DocumentRevision);
        await Assert.That(mcpDocument.Value.Json.Contains(ResourcePolicyUpdateRf3Protocol.SecretValue,
            StringComparison.Ordinal)).IsTrue();
        var query = await McpCallerAssertions.SdkSuccessAsync(await reader.QueryAstAsync(
            ResourcePolicyUpdateRf3Scenario.SecretQuery(scenario.Partition), cancellationToken));
        await Assert.That(query.Rows.Select(row => row.EntityId)).Contains(ResourcePolicyUpdateRf3Protocol.DocumentId);
        var mcpQuery = await McpCallerAssertions.SuccessAsync<QueryPage>(await readerMcp.CallAsync(
            McpCallerTools.QueryAst, ResourcePolicyUpdateRf3Scenario.SecretQuery(scenario.Partition), cancellationToken));
        await Assert.That(mcpQuery.Value.Rows.Select(row => row.EntityId)).Contains(ResourcePolicyUpdateRf3Protocol.DocumentId);
    }

    internal static async Task AssertPolicyAllowedAsync(ClusterFixture fixture,
        ResourcePolicyUpdateRf3Scenario scenario, McpPersistedIdentity reader, McpOfficialClient mcp,
        CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, SecondNode);
        var sdk = new KeyLoadClient(http, reader.Secret);
        var document = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(scenario.Reference, cancellationToken));
        await Assert.That(document!.Redacted).IsFalse();
        await Assert.That(document.Revision).IsEqualTo(scenario.DocumentRevision);
        await Assert.That(document.Json.Contains(ResourcePolicyUpdateRf3Protocol.SecretValue, StringComparison.Ordinal)).IsTrue();
        var mcpDocument = await McpCallerAssertions.SuccessAsync<DocumentResult>(await mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(scenario.Reference), cancellationToken));
        await Assert.That(mcpDocument.Value.Redacted).IsFalse();
        await Assert.That(mcpDocument.Value.Revision).IsEqualTo(scenario.DocumentRevision);
        await Assert.That(mcpDocument.Value.Json.Contains(ResourcePolicyUpdateRf3Protocol.SecretValue,
            StringComparison.Ordinal)).IsTrue();
        var query = await McpCallerAssertions.SdkSuccessAsync(await sdk.QueryAstAsync(
            ResourcePolicyUpdateRf3Scenario.SecretQuery(scenario.Partition), cancellationToken));
        await Assert.That(query.Rows.Select(row => row.EntityId)).Contains(ResourcePolicyUpdateRf3Protocol.DocumentId);
        var mcpQuery = await McpCallerAssertions.SuccessAsync<QueryPage>(await mcp.CallAsync(McpCallerTools.QueryAst,
            ResourcePolicyUpdateRf3Scenario.SecretQuery(scenario.Partition), cancellationToken));
        await Assert.That(mcpQuery.Value.Rows.Select(row => row.EntityId)).Contains(ResourcePolicyUpdateRf3Protocol.DocumentId);
    }

    internal static async Task AssertSameDefinitionAsync(ResourceDefinition expected, ResourceDefinition actual)
    {
        await Assert.That(JsonDefaults.Serialize(actual).AsSpan().SequenceEqual(JsonDefaults.Serialize(expected))).IsTrue();
    }

    internal static async Task AssertPolicyDefinitionAsync(ResourceDefinition original, string readGrant,
        string useGrant, long expectedVersion, ResourceDefinition actual)
    {
        var expected = original with
        {
            FieldPolicies = [new(ResourcePolicyUpdateRf3Protocol.SecretPath,
                ResourcePolicyUpdateRf3Protocol.Classification, readGrant, useGrant)],
            SchemaVersion = expectedVersion
        };
        await AssertSameDefinitionAsync(expected, actual);
        await Assert.That(actual.SchemaVersion).IsEqualTo(expectedVersion);
    }

    internal static async Task AssertSchemaVersionAsync(KeyLoadClient administrator,
        ResourcePolicyUpdateRf3Scenario scenario, long expectedVersion, CancellationToken cancellationToken)
    {
        var resources = await McpCallerAssertions.SdkSuccessAsync(await administrator.ListResourcesAsync(
            new(scenario.Partition.TenantId, scenario.Partition.DatabaseId), cancellationToken));
        await Assert.That(resources.Items.Single(item => item.Name == ResourcePolicyUpdateRf3Protocol.Collection).SchemaVersion)
            .IsEqualTo(expectedVersion);
    }
}
