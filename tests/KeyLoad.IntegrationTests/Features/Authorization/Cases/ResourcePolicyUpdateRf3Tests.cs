using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Authorization;

/// <summary>AC-RPOL-004: policy CAS and current persisted authorization through real SDK and official MCP callers.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class ResourcePolicyUpdateRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task AcRpol004PolicyAckFencesReadersCursorsAndStaleRetriedUpdatesAcrossRf3()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await ResourcePolicyUpdateRf3Scenario.CreateAsync(fixture, deadline.Token);
        await Assert.That(scenario.InitialDefinition.SchemaVersion)
            .IsEqualTo(ResourcePolicyUpdateRf3Protocol.InitialSchemaVersion);
        await using var administratorMcp = await McpOfficialClient.ConnectAsync(fixture,
            McpCallerProtocol.Node1, fixture.AdminKey, deadline.Token);
        await using var readerMcp = await McpOfficialClient.ConnectAsync(fixture,
            McpCallerProtocol.Node3, scenario.Reader.Secret, deadline.Token);
        using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var admin = new KeyLoadClient(adminHttp, fixture.AdminKey);
        await VerifyInitialAccessAsync(fixture, readerMcp, scenario, deadline.Token);
        var cursors = await CaptureCursorsAsync(admin, administratorMcp, scenario, deadline.Token);
        var versionTwo = await UpdateThroughMcpAsync(administratorMcp, scenario,
            ResourcePolicyUpdateRf3Protocol.ReadGrantV2, ResourcePolicyUpdateRf3Protocol.UseGrantV2,
            ResourcePolicyUpdateRf3Protocol.InitialSchemaVersion, deadline.Token);
        await VerifyPolicyAckAsync(fixture, admin, administratorMcp, readerMcp, scenario, versionTwo,
            cursors, deadline.Token);
        await VerifyStaleAndUnauthorizedWritesAsync(fixture, admin, scenario, deadline.Token);
        scenario = scenario with
        {
            Reader = await ResourcePolicyUpdateRf3Scenario.ReplaceReaderGrantsAsync(admin, scenario.Reader,
            [ResourcePolicyUpdateRf3Protocol.ReadGrantV2, ResourcePolicyUpdateRf3Protocol.UseGrantV2], deadline.Token)
        };
        await ResourcePolicyUpdateRf3Assertions.AssertPolicyAllowedAsync(fixture, scenario,
            scenario.Reader, readerMcp, deadline.Token);
        await VerifySdkRetryCannotRestoreOldPolicyAsync(fixture, admin, scenario, versionTwo, deadline.Token);
        scenario = scenario with
        {
            Reader = await ResourcePolicyUpdateRf3Scenario.ReplaceReaderGrantsAsync(admin, scenario.Reader,
            [ResourcePolicyUpdateRf3Protocol.ReadGrantV4, ResourcePolicyUpdateRf3Protocol.UseGrantV4], deadline.Token)
        };
        await ResourcePolicyUpdateRf3Assertions.AssertPolicyAllowedAsync(fixture, scenario,
            scenario.Reader, readerMcp, deadline.Token);
    }

    private static async Task VerifyInitialAccessAsync(ClusterFixture fixture, McpOfficialClient readerMcp,
        ResourcePolicyUpdateRf3Scenario scenario, CancellationToken cancellationToken)
    {
        await ResourcePolicyUpdateRf3Assertions.AssertPolicyAllowedAsyncFromInitialAsync(fixture, readerMcp,
            scenario, cancellationToken);
    }

    private static async Task<ResourcePolicyUpdateCursors> CaptureCursorsAsync(KeyLoadClient admin,
        McpOfficialClient administratorMcp, ResourcePolicyUpdateRf3Scenario scenario,
        CancellationToken cancellationToken)
        => await ResourcePolicyUpdateRf3Scenario.CaptureCursorsAsync(admin, administratorMcp, scenario, cancellationToken);

    private static async Task<ResourceDefinition> UpdateThroughMcpAsync(McpOfficialClient administratorMcp,
        ResourcePolicyUpdateRf3Scenario scenario, string readGrant, string useGrant, long expectedVersion,
        CancellationToken cancellationToken)
        => await ResourcePolicyUpdateRf3Scenario.UpdateThroughMcpAsync(administratorMcp, scenario,
            readGrant, useGrant, expectedVersion, cancellationToken);

    private static async Task VerifyPolicyAckAsync(ClusterFixture fixture, KeyLoadClient admin,
        McpOfficialClient administratorMcp, McpOfficialClient readerMcp, ResourcePolicyUpdateRf3Scenario scenario,
        ResourceDefinition updated, ResourcePolicyUpdateCursors cursors, CancellationToken cancellationToken)
    {
        await ResourcePolicyUpdateRf3Assertions.AssertPolicyDefinitionAsync(scenario.InitialDefinition,
            ResourcePolicyUpdateRf3Protocol.ReadGrantV2, ResourcePolicyUpdateRf3Protocol.UseGrantV2,
            ResourcePolicyUpdateRf3Protocol.AfterMcpUpdateSchemaVersion, updated);
        await ResourcePolicyUpdateRf3Assertions.AssertSchemaVersionAsync(admin, scenario,
            ResourcePolicyUpdateRf3Protocol.AfterMcpUpdateSchemaVersion, cancellationToken);
        await ResourcePolicyUpdateRf3Assertions.AssertPolicyDeniedAsync(fixture, scenario,
            scenario.Reader, readerMcp, cancellationToken);
        await ResourcePolicyUpdateRf3Scenario.AssertCursorsInvalidatedAsync(admin, administratorMcp,
            scenario, cursors, cancellationToken);
    }

    private static async Task VerifyStaleAndUnauthorizedWritesAsync(ClusterFixture fixture, KeyLoadClient admin,
        ResourcePolicyUpdateRf3Scenario scenario, CancellationToken cancellationToken)
    {
        using var readerHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var reader = new KeyLoadClient(readerHttp, scenario.Reader.Secret);
        var stale = scenario.Request(ResourcePolicyUpdateRf3Protocol.StaleGrant,
            ResourcePolicyUpdateRf3Protocol.StaleGrant, ResourcePolicyUpdateRf3Protocol.InitialSchemaVersion);
        var staleResult = await admin.ConfigureResourceAsync(Guid.NewGuid(), stale, cancellationToken);
        await Assert.That(staleResult.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.RevisionConflict));
        var unauthorized = scenario.Request(ResourcePolicyUpdateRf3Protocol.StaleGrant,
            ResourcePolicyUpdateRf3Protocol.StaleGrant, ResourcePolicyUpdateRf3Protocol.AfterMcpUpdateSchemaVersion);
        var denied = await reader.ConfigureResourceAsync(Guid.NewGuid(), unauthorized, cancellationToken);
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await ResourcePolicyUpdateRf3Assertions.AssertSchemaVersionAsync(admin, scenario,
            ResourcePolicyUpdateRf3Protocol.AfterMcpUpdateSchemaVersion, cancellationToken);
    }

    private static async Task VerifySdkRetryCannotRestoreOldPolicyAsync(ClusterFixture fixture,
        KeyLoadClient admin, ResourcePolicyUpdateRf3Scenario scenario, ResourceDefinition versionTwo,
        CancellationToken cancellationToken)
        => await ResourcePolicyUpdateRf3Scenario.VerifySdkRetryCannotRestoreOldPolicyAsync(fixture, admin,
            scenario, versionTwo, cancellationToken);
}
