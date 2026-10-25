using System.Collections.Immutable;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.IntegrationTests.ResourceExecution;
using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.Authorization;

/// <summary>Owns the actual persisted Orleans partition, scoped reader and public update calls.</summary>
internal sealed record ResourcePolicyUpdateClusterScenario(PartitionRef Partition, EntityRef Reference,
    ResourceDefinition InitialDefinition, McpPersistedIdentity Reader, long DocumentRevision, EntityRefLifetime Lifetime)
{
    private const string TenantPrefix = "resource-policy-cluster-tenant-";
    private const string DatabaseId = "resource-policy-cluster-database";
    private const string DomainId = "resource-policy-cluster-domain";

    internal static async Task<ResourcePolicyUpdateClusterScenario> CreateAsync(ClusterFixture fixture,
        CancellationToken cancellationToken)
    {
        var partition = new PartitionRef(TenantPrefix + Guid.NewGuid().ToString("N"), DatabaseId,
            DomainId, Guid.NewGuid().ToString("N"));
        var definition = Definition(partition, ResourcePolicyUpdateClusterProtocol.InitialReadGrant,
            ResourcePolicyUpdateClusterProtocol.InitialUseGrant);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var creationId = Guid.NewGuid();
        var creation = new ConfigureResourceRequest(partition.TenantId, partition.DatabaseId, definition);
        var persistedDefinition = await McpCallerAssertions.SdkSuccessAsync(await admin.ConfigureResourceAsync(creationId,
            creation, cancellationToken));
        await McpCallerAssertions.SdkSuccessAsync(await admin.CommitAsync(Seed(partition), cancellationToken));
        var identity = await McpPersistedIdentity.CreateAsync(fixture, partition,
            ResourcePolicyUpdateClusterProtocol.Collection,
            Capability.DocumentsRead | Capability.Query | Capability.ChangesRead, cancellationToken);
        var reader = await ReplaceReaderGrantsAsync(admin, identity,
            [ResourcePolicyUpdateClusterProtocol.InitialReadGrant, ResourcePolicyUpdateClusterProtocol.InitialUseGrant], cancellationToken);
        var reference = new EntityRef(partition, ResourcePolicyUpdateClusterProtocol.Collection,
            ResourcePolicyUpdateClusterProtocol.DocumentId);
        var document = await PublicReadAssertions.DocumentAsync(await admin.GetAsync(reference, cancellationToken));
        return new(partition, reference, persistedDefinition, reader, document!.Revision,
            ResourceCreationLifetime.Create(creationId, creation));
    }

    internal EntityRef StoredReference(string id)
        => new(Partition, ResourcePolicyUpdateClusterProtocol.Collection, id) { Lifetime = Lifetime };

    internal static async Task<McpPersistedIdentity> ReplaceReaderGrantsAsync(KeyLoadClient administrator,
        McpPersistedIdentity identity, ImmutableArray<string> grants, CancellationToken cancellationToken)
    {
        var principal = identity.Principal with
        {
            FieldGrants = grants,
            PolicyEpoch = checked(identity.Principal.PolicyEpoch + 1)
        };
        var persisted = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(),
            principal, cancellationToken));
        await Assert.That(persisted.PolicyEpoch).IsEqualTo(principal.PolicyEpoch);
        return identity with
        {
            Principal = principal
        };
    }

    internal ConfigureResourceRequest Request(string readGrant, string useGrant, long expectedVersion)
        => new(Partition.TenantId, Partition.DatabaseId,
            InitialDefinition with
            {
                FieldPolicies = [new(ResourcePolicyUpdateClusterProtocol.SecretPath,
                    ResourcePolicyUpdateClusterProtocol.Classification, readGrant, useGrant)],
                SchemaVersion = checked(expectedVersion + 1)
            })
        {
            ExpectedSchemaVersion = expectedVersion
        };

    internal static async Task<ResourcePolicyUpdateCursors> CaptureCursorsAsync(KeyLoadClient administrator,
        McpOfficialClient administratorMcp, ResourcePolicyUpdateClusterScenario scenario,
        CancellationToken cancellationToken)
    {
        var sdkChanges = await McpCallerAssertions.SdkSuccessAsync(await administrator.ReadChangesAsync(
            new(scenario.Partition, ResourcePolicyUpdateClusterProtocol.Collection, Start: ChangeFeedStart.Now), cancellationToken));
        var mcpChanges = await McpCallerAssertions.SuccessAsync<ChangeFeedPage>(await administratorMcp.CallAsync(
            McpCallerTools.ChangesRead,
            new ReadChangeFeedRequest(scenario.Partition, ResourcePolicyUpdateClusterProtocol.Collection,
                Start: ChangeFeedStart.Now), cancellationToken));
        var liveRequest = new AstQueryRequest(scenario.Partition,
            new SelectQuery(ResourcePolicyUpdateClusterProtocol.Collection, null, [new("*", "*")], null, [], 10),
            AllowFullScan: true);
        var sdkLive = await McpCallerAssertions.SdkSuccessAsync(await administrator.StartLiveQueryAsync(
            new(liveRequest), cancellationToken));
        var mcpLive = await McpCallerAssertions.SuccessAsync<LiveQuerySnapshot>(await administratorMcp.CallAsync(
            McpCallerTools.QueryLiveStart, new StartLiveQueryRequest(liveRequest), cancellationToken));
        return new(sdkChanges.Cursor, mcpChanges.Value.Cursor, liveRequest, sdkLive.Cursor, mcpLive.Value.Cursor);
    }

    internal static async Task<ResourceDefinition> UpdateThroughMcpAsync(McpOfficialClient administratorMcp,
        ResourcePolicyUpdateClusterScenario scenario, string readGrant, string useGrant, long expectedVersion,
        CancellationToken cancellationToken)
    {
        var request = scenario.Request(readGrant, useGrant, expectedVersion);
        var arguments = McpOfficialClient.Arguments(request);
        arguments.Add(McpCallerProtocol.CommandId, Guid.NewGuid());
        var reply = await administratorMcp.Client.InvokeKeyLoadToolAsync(McpCallerTools.ResourcesConfigure,
            arguments, cancellationToken: cancellationToken);
        var receipt = await McpCallerAssertions.SuccessAsync<ResourceDefinition>(reply);
        await ResourcePolicyUpdateClusterAssertions.AssertSameDefinitionAsync(request.Definition, receipt.Value);
        return receipt.Value;
    }

    internal static async Task AssertCursorsInvalidatedAsync(KeyLoadClient administrator,
        McpOfficialClient administratorMcp, ResourcePolicyUpdateClusterScenario scenario,
        ResourcePolicyUpdateCursors cursors, CancellationToken cancellationToken)
    {
        var sdkChanges = await administrator.ReadChangesAsync(new(scenario.Partition,
            ResourcePolicyUpdateClusterProtocol.Collection, cursors.SdkChange), cancellationToken);
        await Assert.That(sdkChanges.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
        var sdkLive = await administrator.ReadLiveQueryAsync(new(cursors.LiveRequest, cursors.SdkLive), cancellationToken);
        await Assert.That(sdkLive.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
        _ = (await McpCallerAssertions.ErrorAsync(await administratorMcp.CallAsync(McpCallerTools.ChangesRead,
            new ReadChangeFeedRequest(scenario.Partition, ResourcePolicyUpdateClusterProtocol.Collection,
                cursors.McpChange), cancellationToken), ErrorCode.TokenInvalidated, dispatched: true)).Value;
        _ = (await McpCallerAssertions.ErrorAsync(await administratorMcp.CallAsync(McpCallerTools.QueryLiveRead,
            new ReadLiveQueryRequest(cursors.LiveRequest, cursors.McpLive), cancellationToken),
            ErrorCode.TokenInvalidated, dispatched: true)).Value;
    }

    internal static async Task VerifySdkRetryCannotRestoreOldPolicyAsync(ClusterFixture fixture,
        KeyLoadClient administrator, ResourcePolicyUpdateClusterScenario scenario, ResourceDefinition versionTwo,
        CancellationToken cancellationToken)
    {
        var versionThreeRequest = scenario.Request(ResourcePolicyUpdateClusterProtocol.SdkReadGrant,
            ResourcePolicyUpdateClusterProtocol.SdkUseGrant, versionTwo.SchemaVersion);
        var originalCommandId = Guid.NewGuid();
        var first = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(
            originalCommandId, versionThreeRequest, cancellationToken));
        await ResourcePolicyUpdateClusterAssertions.AssertSameDefinitionAsync(versionThreeRequest.Definition, first);
        var versionFourRequest = scenario.Request(ResourcePolicyUpdateClusterProtocol.CurrentReadGrant,
            ResourcePolicyUpdateClusterProtocol.CurrentUseGrant, first.SchemaVersion);
        var current = await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureResourceAsync(
            Guid.NewGuid(), versionFourRequest, cancellationToken));
        await ResourcePolicyUpdateClusterAssertions.AssertSameDefinitionAsync(versionFourRequest.Definition, current);
        await RetryOriginalCommandAsync(fixture, originalCommandId, versionThreeRequest, first, cancellationToken);
        await ResourcePolicyUpdateClusterAssertions.AssertSchemaVersionAsync(administrator, scenario,
            ResourcePolicyUpdateClusterProtocol.CurrentSchemaVersion, cancellationToken);
        await AssertCurrentFieldsAsync(administrator, scenario, current, cancellationToken);
    }

    private static async Task RetryOriginalCommandAsync(ClusterFixture fixture, Guid commandId,
        ConfigureResourceRequest request, ResourceDefinition originalResult, CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node3);
        var retry = await McpCallerAssertions.SdkSuccessAsync(await new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution())
            .ConfigureResourceAsync(commandId, request, cancellationToken));
        await ResourcePolicyUpdateClusterAssertions.AssertSameDefinitionAsync(originalResult, retry);
    }

    private static async Task AssertCurrentFieldsAsync(KeyLoadClient administrator,
        ResourcePolicyUpdateClusterScenario scenario, ResourceDefinition actual, CancellationToken cancellationToken)
    {
        var listed = await McpCallerAssertions.SdkSuccessAsync(await administrator.ListResourcesAsync(
            new(scenario.Partition.TenantId, scenario.Partition.DatabaseId), cancellationToken));
        await Assert.That(listed.Items.Single(item => item.Name == ResourcePolicyUpdateClusterProtocol.Collection).SchemaVersion)
            .IsEqualTo(actual.SchemaVersion);
        await ResourcePolicyUpdateClusterAssertions.AssertSameDefinitionAsync(actual,
            scenario.InitialDefinition with
            {
                FieldPolicies = actual.FieldPolicies,
                SchemaVersion = actual.SchemaVersion
            });
    }

    internal static ResourceDefinition Definition(PartitionRef partition, string readGrant, string useGrant)
        => new(ResourcePolicyUpdateClusterProtocol.Collection, ResourceKind.Collection, partition.TransactionDomainId)
        {
            FieldPolicies = [new(ResourcePolicyUpdateClusterProtocol.SecretPath,
                ResourcePolicyUpdateClusterProtocol.Classification, readGrant, useGrant)]
        };

    internal static CommandRequest Seed(PartitionRef partition)
        => new(Guid.NewGuid(), partition,
        [new PutDocument(ResourcePolicyUpdateClusterProtocol.Collection,
            ResourcePolicyUpdateClusterProtocol.DocumentId,
            ResourcePolicyUpdateClusterProtocol.SeedJson)]);

    internal static AstQueryRequest SecretQuery(PartitionRef partition)
        => new(partition, new SelectQuery(ResourcePolicyUpdateClusterProtocol.Collection, null, [new("*", "*")],
            new Comparison(new FieldOperand(ResourcePolicyUpdateClusterProtocol.SecretPath),
                ResourcePolicyUpdateClusterProtocol.QueryOperator,
                ValueOperand.Create(ResourcePolicyUpdateClusterProtocol.SecretValue)), [],
            ResourcePolicyUpdateClusterProtocol.QueryLimit), AllowFullScan: true);
}
