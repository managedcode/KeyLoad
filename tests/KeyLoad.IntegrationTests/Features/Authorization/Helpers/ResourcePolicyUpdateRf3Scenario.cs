using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Features.Authorization;

/// <summary>Owns the actual persisted RF3 partition, scoped reader and public update calls.</summary>
internal sealed record ResourcePolicyUpdateRf3Scenario(PartitionRef Partition, EntityRef Reference,
    ResourceDefinition InitialDefinition, McpPersistedIdentity Reader, long DocumentRevision)
{
    private const string TenantPrefix = "resource-policy-rf3-tenant-";
    private const string DatabaseId = "resource-policy-rf3-database";
    private const string DomainId = "resource-policy-rf3-domain";

    internal static async Task<ResourcePolicyUpdateRf3Scenario> CreateAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var partition = new PartitionRef(TenantPrefix + Guid.NewGuid().ToString("N"), DatabaseId,
            DomainId, Guid.NewGuid().ToString("N"));
        var definition = Definition(partition, ResourcePolicyUpdateRf3Protocol.ReadGrantV1,
            ResourcePolicyUpdateRf3Protocol.UseGrantV1);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var persistedDefinition = await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigureResourceAsync(Guid.NewGuid(),
            new(partition.TenantId, partition.DatabaseId, definition), cancellationToken));
        await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(Seed(partition), cancellationToken));
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition,
            ResourcePolicyUpdateRf3Protocol.Collection,
            Capability.DocumentsRead | Capability.Query | Capability.ChangesRead, cancellationToken);
        var reader = await ReplaceReaderGrantsAsync(admin, identity,
            [ResourcePolicyUpdateRf3Protocol.ReadGrantV1, ResourcePolicyUpdateRf3Protocol.UseGrantV1], cancellationToken);
        var reference = new EntityRef(partition, ResourcePolicyUpdateRf3Protocol.Collection,
            ResourcePolicyUpdateRf3Protocol.DocumentId);
        var document = await McpCallerAssertions.SdkSuccessAsync(await admin.GetAsync(reference, cancellationToken));
        return new(partition, reference, persistedDefinition, reader, document!.Revision);
    }

    internal static async Task<McpPersistedIdentity> ReplaceReaderGrantsAsync(KeyLoadClient administrator,
        McpPersistedIdentity identity, ImmutableArray<string> grants, CancellationToken cancellationToken)
    {
        var principal = identity.Principal with
        { FieldGrants = grants, PolicyEpoch = checked(identity.Principal.PolicyEpoch + 1) };
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(),
            principal, cancellationToken));
        await Assert.That(persisted.PolicyEpoch).IsEqualTo(principal.PolicyEpoch);
        return identity with { Principal = principal };
    }

    internal ConfigureResourceRequest Request(string readGrant, string useGrant, long expectedVersion)
        => new(Partition.TenantId, Partition.DatabaseId,
            InitialDefinition with
            {
                FieldPolicies = [new(ResourcePolicyUpdateRf3Protocol.SecretPath,
                    ResourcePolicyUpdateRf3Protocol.Classification, readGrant, useGrant)],
                SchemaVersion = checked(expectedVersion + 1)
            })
        { ExpectedSchemaVersion = expectedVersion };

    internal static async Task<ResourcePolicyUpdateCursors> CaptureCursorsAsync(KeyLoadClient administrator,
        McpOfficialClient administratorMcp, ResourcePolicyUpdateRf3Scenario scenario,
        CancellationToken cancellationToken)
    {
        var sdkChanges = await McpCallerAssertions.SdkSuccessAsync(await administrator.ReadChangesAsync(
            new(scenario.Partition, ResourcePolicyUpdateRf3Protocol.Collection, Start: ChangeFeedStart.Now), cancellationToken));
        var mcpChanges = await McpCallerAssertions.SuccessAsync<ChangeFeedPage>(await administratorMcp.CallAsync(
            McpCallerTools.ChangesRead,
            new ReadChangeFeedRequest(scenario.Partition, ResourcePolicyUpdateRf3Protocol.Collection,
                Start: ChangeFeedStart.Now), cancellationToken));
        var liveRequest = new AstQueryRequest(scenario.Partition,
            new SelectQuery(ResourcePolicyUpdateRf3Protocol.Collection, null, [new("*", "*")], null, [], 10),
            AllowFullScan: true);
        var sdkLive = await McpCallerAssertions.SdkSuccessAsync(await administrator.StartLiveQueryAsync(
            new(liveRequest), cancellationToken));
        var mcpLive = await McpCallerAssertions.SuccessAsync<LiveQuerySnapshot>(await administratorMcp.CallAsync(
            McpCallerTools.QueryLiveStart, new StartLiveQueryRequest(liveRequest), cancellationToken));
        return new(sdkChanges.Cursor, mcpChanges.Value.Cursor, liveRequest, sdkLive.Cursor, mcpLive.Value.Cursor);
    }

    internal static async Task<ResourceDefinition> UpdateThroughMcpAsync(McpOfficialClient administratorMcp,
        ResourcePolicyUpdateRf3Scenario scenario, string readGrant, string useGrant, long expectedVersion,
        CancellationToken cancellationToken)
    {
        var request = scenario.Request(readGrant, useGrant, expectedVersion);
        var arguments = McpOfficialClient.Arguments(request);
        arguments.Add(McpCallerProtocol.CommandId, Guid.NewGuid());
        var reply = await administratorMcp.Client.InvokeKeyLoadToolAsync(McpCallerTools.ResourcesConfigure,
            arguments, cancellationToken: cancellationToken);
        var receipt = await McpCallerAssertions.SuccessAsync<ResourceDefinition>(reply);
        await ResourcePolicyUpdateRf3Assertions.AssertSameDefinitionAsync(request.Definition, receipt.Value);
        return receipt.Value;
    }

    internal static async Task AssertCursorsInvalidatedAsync(KeyLoadClient administrator,
        McpOfficialClient administratorMcp, ResourcePolicyUpdateRf3Scenario scenario,
        ResourcePolicyUpdateCursors cursors, CancellationToken cancellationToken)
    {
        var sdkChanges = await administrator.ReadChangesAsync(new(scenario.Partition,
            ResourcePolicyUpdateRf3Protocol.Collection, cursors.SdkChange), cancellationToken);
        await Assert.That(sdkChanges.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
        var sdkLive = await administrator.ReadLiveQueryAsync(new(cursors.LiveRequest, cursors.SdkLive), cancellationToken);
        await Assert.That(sdkLive.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
        await McpCallerAssertions.ErrorAsync(await administratorMcp.CallAsync(McpCallerTools.ChangesRead,
            new ReadChangeFeedRequest(scenario.Partition, ResourcePolicyUpdateRf3Protocol.Collection,
                cursors.McpChange), cancellationToken), ErrorCode.TokenInvalidated, dispatched: true);
        await McpCallerAssertions.ErrorAsync(await administratorMcp.CallAsync(McpCallerTools.QueryLiveRead,
            new ReadLiveQueryRequest(cursors.LiveRequest, cursors.McpLive), cancellationToken),
            ErrorCode.TokenInvalidated, dispatched: true);
    }

    internal static async Task VerifySdkRetryCannotRestoreOldPolicyAsync(ClusterFixture fixture,
        KeyLoadClient administrator, ResourcePolicyUpdateRf3Scenario scenario, ResourceDefinition versionTwo,
        CancellationToken cancellationToken)
    {
        var versionThreeRequest = scenario.Request(ResourcePolicyUpdateRf3Protocol.ReadGrantV3,
            ResourcePolicyUpdateRf3Protocol.UseGrantV3, versionTwo.SchemaVersion);
        var originalCommandId = Guid.NewGuid();
        var first = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(
            originalCommandId, versionThreeRequest, cancellationToken));
        await ResourcePolicyUpdateRf3Assertions.AssertSameDefinitionAsync(versionThreeRequest.Definition, first);
        var versionFourRequest = scenario.Request(ResourcePolicyUpdateRf3Protocol.ReadGrantV4,
            ResourcePolicyUpdateRf3Protocol.UseGrantV4, first.SchemaVersion);
        var current = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(
            Guid.NewGuid(), versionFourRequest, cancellationToken));
        await ResourcePolicyUpdateRf3Assertions.AssertSameDefinitionAsync(versionFourRequest.Definition, current);
        await RetryOriginalCommandAsync(fixture, originalCommandId, versionThreeRequest, first, cancellationToken);
        await ResourcePolicyUpdateRf3Assertions.AssertSchemaVersionAsync(administrator, scenario,
            ResourcePolicyUpdateRf3Protocol.CurrentSchemaVersion, cancellationToken);
        await AssertCurrentFieldsAsync(administrator, scenario, current, cancellationToken);
    }

    private static async Task RetryOriginalCommandAsync(ClusterFixture fixture, Guid commandId,
        ConfigureResourceRequest request, ResourceDefinition originalResult, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node3);
        var retry = await McpCallerAssertions.SdkSuccessAsync(await new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution())
            .ConfigureResourceAsync(commandId, request, cancellationToken));
        await ResourcePolicyUpdateRf3Assertions.AssertSameDefinitionAsync(originalResult, retry);
    }

    private static async Task AssertCurrentFieldsAsync(KeyLoadClient administrator,
        ResourcePolicyUpdateRf3Scenario scenario, ResourceDefinition actual, CancellationToken cancellationToken)
    {
        var listed = await McpCallerAssertions.SdkSuccessAsync(await administrator.ListResourcesAsync(
            new(scenario.Partition.TenantId, scenario.Partition.DatabaseId), cancellationToken));
        await Assert.That(listed.Items.Single(item => item.Name == ResourcePolicyUpdateRf3Protocol.Collection).SchemaVersion)
            .IsEqualTo(actual.SchemaVersion);
        await ResourcePolicyUpdateRf3Assertions.AssertSameDefinitionAsync(actual,
            scenario.InitialDefinition with { FieldPolicies = actual.FieldPolicies, SchemaVersion = actual.SchemaVersion });
    }

    internal static ResourceDefinition Definition(PartitionRef partition, string readGrant, string useGrant)
        => new(ResourcePolicyUpdateRf3Protocol.Collection, ResourceKind.Collection, partition.TransactionDomainId)
        {
            FieldPolicies = [new(ResourcePolicyUpdateRf3Protocol.SecretPath,
                ResourcePolicyUpdateRf3Protocol.Classification, readGrant, useGrant)]
        };

    internal static CommandRequest Seed(PartitionRef partition)
        => new(Guid.NewGuid(), partition,
        [new PutDocument(ResourcePolicyUpdateRf3Protocol.Collection,
            ResourcePolicyUpdateRf3Protocol.DocumentId,
            ResourcePolicyUpdateRf3Protocol.SeedJson)]);

    internal static AstQueryRequest SecretQuery(PartitionRef partition)
        => new(partition, new SelectQuery(ResourcePolicyUpdateRf3Protocol.Collection, null, [new("*", "*")],
            new Comparison(new FieldOperand(ResourcePolicyUpdateRf3Protocol.SecretPath),
                ResourcePolicyUpdateRf3Protocol.QueryOperator,
                ValueOperand.Create(ResourcePolicyUpdateRf3Protocol.SecretValue)), [],
            ResourcePolicyUpdateRf3Protocol.QueryLimit), AllowFullScan: true);
}
