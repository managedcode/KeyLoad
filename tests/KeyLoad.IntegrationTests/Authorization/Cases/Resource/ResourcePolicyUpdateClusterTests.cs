using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Authorization;

/// <summary>AC-RPOL-004: policy CAS and current persisted authorization through real SDK and official MCP callers.</summary>
[NotInParallel]
internal sealed class ResourcePolicyUpdateClusterTests : IAsyncDisposable
{
    private readonly ClusterFixture fixture = new();

    [Test]
    public async Task AcRpol004PolicyAckFencesReadersCursorsAndStaleRetriedUpdatesAcrossCluster()
    {
        await fixture.InitializeAsync();
        using var deadline = McpCallerDeadline.Create();
        var state = await WarmAsync(deadline.Token);
        await PolicyEpochPublicCold.RunAsync(fixture, state, deadline.Token);
        await PolicyEpochPublicCold.RunAsync(fixture, state, deadline.Token);
        await QueryOnlyEpochR46Flow.RunAsync(fixture, state, deadline.Token);
    }

    public ValueTask DisposeAsync() => fixture.DisposeAsync();

    private async Task<PolicyEpochPublicState> WarmAsync(CancellationToken token)
    {
        var scenario = await ResourcePolicyUpdateClusterScenario.CreateAsync(fixture, token);
        await Assert.That(scenario.InitialDefinition.SchemaVersion)
            .IsEqualTo(ResourcePolicyUpdateClusterProtocol.InitialSchemaVersion);
        var failures = new List<Exception>();
        PolicyEpochPublicState? result = null;
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            await using var administratorMcp = await McpOfficialClient.ConnectAsync(fixture,
                McpCallerProtocol.Node1, fixture.AdminKey, token);
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
            {
                await using var readerMcp = await McpOfficialClient.ConnectAsync(fixture,
                    McpCallerProtocol.Node3, scenario.Reader.Secret, token);
                await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                    result = await WarmHttpAsync(administratorMcp, readerMcp, scenario, token), failures);
            }, failures);
        }, failures);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return result!;
    }

    private async Task<PolicyEpochPublicState> WarmHttpAsync(McpOfficialClient administratorMcp,
        McpOfficialClient readerMcp, ResourcePolicyUpdateClusterScenario scenario, CancellationToken token)
    {
        var failures = new List<Exception>();
        PolicyEpochPublicState? result = null;
        await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
        {
            using var adminHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
            var admin = new KeyLoadClient(adminHttp, fixture.AdminKey, IntegrationClientOptions.Execution());
            await KeyLoad.Server.ServerFailureObserver.ObserveAsync(async () =>
                result = await WarmCallsAsync(admin, administratorMcp, readerMcp, scenario, token), failures);
        }, failures);
        KeyLoad.Server.ServerFailureObserver.ThrowIfAny(failures);
        return result!;
    }

    private async Task<PolicyEpochPublicState> WarmCallsAsync(KeyLoadClient admin,
        McpOfficialClient administratorMcp, McpOfficialClient readerMcp,
        ResourcePolicyUpdateClusterScenario scenario, CancellationToken token)
    {
        await VerifyInitialAccessAsync(fixture, readerMcp, scenario, token);
        var cursors = await CaptureCursorsAsync(admin, administratorMcp, scenario, token);
        var versionTwo = await UpdateThroughMcpAsync(administratorMcp, scenario,
            ResourcePolicyUpdateClusterProtocol.McpReadGrant, ResourcePolicyUpdateClusterProtocol.McpUseGrant,
            ResourcePolicyUpdateClusterProtocol.InitialSchemaVersion, token);
        await VerifyPolicyAckAsync(fixture, admin, administratorMcp, readerMcp, scenario, versionTwo,
            cursors, token);
        await VerifyStaleAndUnauthorizedWritesAsync(fixture, admin, scenario, token);
        scenario = scenario with
        {
            Reader = await ResourcePolicyUpdateClusterScenario.ReplaceReaderGrantsAsync(admin, scenario.Reader,
            [ResourcePolicyUpdateClusterProtocol.McpReadGrant, ResourcePolicyUpdateClusterProtocol.McpUseGrant], token)
        };
        await ResourcePolicyUpdateClusterAssertions.AssertPolicyAllowedAsync(fixture, scenario,
            scenario.Reader, readerMcp, token);
        await VerifySdkRetryCannotRestoreOldPolicyAsync(fixture, admin, scenario, versionTwo, token);
        scenario = scenario with
        {
            Reader = await ResourcePolicyUpdateClusterScenario.ReplaceReaderGrantsAsync(admin, scenario.Reader,
            [ResourcePolicyUpdateClusterProtocol.CurrentReadGrant, ResourcePolicyUpdateClusterProtocol.CurrentUseGrant], token)
        };
        await ResourcePolicyUpdateClusterAssertions.AssertPolicyAllowedAsync(fixture, scenario,
            scenario.Reader, readerMcp, token);
        return await PolicyEpochPublicContinuation.RunAsync(fixture, admin, scenario, token);
    }

    private static async Task VerifyInitialAccessAsync(ClusterFixture fixture, McpOfficialClient readerMcp,
        ResourcePolicyUpdateClusterScenario scenario, CancellationToken cancellationToken)
    {
        await ResourcePolicyUpdateClusterAssertions.AssertPolicyAllowedAsyncFromInitialAsync(fixture, readerMcp,
            scenario, cancellationToken);
    }

    private static async Task<ResourcePolicyUpdateCursors> CaptureCursorsAsync(KeyLoadClient admin,
        McpOfficialClient administratorMcp, ResourcePolicyUpdateClusterScenario scenario,
        CancellationToken cancellationToken)
        => await ResourcePolicyUpdateClusterScenario.CaptureCursorsAsync(admin, administratorMcp, scenario, cancellationToken);

    private static async Task<ResourceDefinition> UpdateThroughMcpAsync(McpOfficialClient administratorMcp,
        ResourcePolicyUpdateClusterScenario scenario, string readGrant, string useGrant, long expectedVersion,
        CancellationToken cancellationToken)
        => await ResourcePolicyUpdateClusterScenario.UpdateThroughMcpAsync(administratorMcp, scenario,
            readGrant, useGrant, expectedVersion, cancellationToken);

    private static async Task VerifyPolicyAckAsync(ClusterFixture fixture, KeyLoadClient admin,
        McpOfficialClient administratorMcp, McpOfficialClient readerMcp, ResourcePolicyUpdateClusterScenario scenario,
        ResourceDefinition updated, ResourcePolicyUpdateCursors cursors, CancellationToken cancellationToken)
    {
        await ResourcePolicyUpdateClusterAssertions.AssertPolicyDefinitionAsync(scenario.InitialDefinition,
            ResourcePolicyUpdateClusterProtocol.McpReadGrant, ResourcePolicyUpdateClusterProtocol.McpUseGrant,
            ResourcePolicyUpdateClusterProtocol.AfterMcpUpdateSchemaVersion, updated);
        await ResourcePolicyUpdateClusterAssertions.AssertSchemaVersionAsync(admin, scenario,
            ResourcePolicyUpdateClusterProtocol.AfterMcpUpdateSchemaVersion, cancellationToken);
        await ResourcePolicyUpdateClusterAssertions.AssertPolicyDeniedAsync(fixture, scenario,
            scenario.Reader, readerMcp, cancellationToken);
        await ResourcePolicyUpdateClusterScenario.AssertCursorsInvalidatedAsync(admin, administratorMcp,
            scenario, cursors, cancellationToken);
    }

    private static async Task VerifyStaleAndUnauthorizedWritesAsync(ClusterFixture fixture, KeyLoadClient admin,
        ResourcePolicyUpdateClusterScenario scenario, CancellationToken cancellationToken)
    {
        using var readerHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var reader = new KeyLoadClient(readerHttp, scenario.Reader.Secret, IntegrationClientOptions.Execution());
        var stale = scenario.Request(ResourcePolicyUpdateClusterProtocol.StaleGrant,
            ResourcePolicyUpdateClusterProtocol.StaleGrant, ResourcePolicyUpdateClusterProtocol.InitialSchemaVersion);
        var staleResult = await admin.ConfigureResourceAsync(Guid.NewGuid(), stale, cancellationToken);
        await Assert.That(staleResult.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.RevisionConflict));
        var unauthorized = scenario.Request(ResourcePolicyUpdateClusterProtocol.StaleGrant,
            ResourcePolicyUpdateClusterProtocol.StaleGrant, ResourcePolicyUpdateClusterProtocol.AfterMcpUpdateSchemaVersion);
        var denied = await reader.ConfigureResourceAsync(Guid.NewGuid(), unauthorized, cancellationToken);
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await ResourcePolicyUpdateClusterAssertions.AssertSchemaVersionAsync(admin, scenario,
            ResourcePolicyUpdateClusterProtocol.AfterMcpUpdateSchemaVersion, cancellationToken);
    }

    private static async Task VerifySdkRetryCannotRestoreOldPolicyAsync(ClusterFixture fixture,
        KeyLoadClient admin, ResourcePolicyUpdateClusterScenario scenario, ResourceDefinition versionTwo,
        CancellationToken cancellationToken)
        => await ResourcePolicyUpdateClusterScenario.VerifySdkRetryCannotRestoreOldPolicyAsync(fixture, admin,
            scenario, versionTwo, cancellationToken);
}
