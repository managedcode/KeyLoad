using System.Net;
using KeyLoad.IntegrationTests.Features.Authorization;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>AC-MCP-002: real native callers cannot retain authority from an earlier discovery request.</summary>
/// <param name="fixture">The initialized actual Docker/Aspire Orleans application.</param>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class McpCredentialLifecycleTests(ClusterFixture fixture)
{
    /// <summary>Both persisted revocation and expiry reject a previously successful official client before operation dispatch.</summary>
    /// <param name="expire">Selects an actual past expiration instead of credential revocation.</param>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcMcp002PersistedCredentialChangesRejectTheClientAfterDiscovery(bool expire)
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await McpDocumentScenario.CreateAsync(fixture, deadline.Token);
        await scenario.SeedAsync(fixture, deadline.Token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition, Capability.DocumentsRead, deadline.Token);
        var repairId = Guid.NewGuid();
        var state = new CredentialAuthorityState(new(scenario, identity, repairId, identity.Principal), repairId);
        await CredentialAuthorityFlow.RunAsync(fixture, state, expire, deadline.Token);
    }

    /// <summary>Missing and unconfigured credentials fail during native discovery with safe HTTP rejection.</summary>
    /// <param name="missing">Selects an absent bearer header instead of a fresh unknown key.</param>
    [Test]
    [Arguments(false)]
    [Arguments(true)]
    public async Task AcMcp002NativeDiscoveryRejectsMissingAndUnconfiguredCredentials(bool missing)
    {
        using var deadline = McpCallerDeadline.Create();
        var key = missing ? null : McpCallerProtocol.UnconfiguredCredentialPrefix + Guid.NewGuid().ToString(McpCallerProtocol.GuidFormat);
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => McpOfficialClient.ConnectAsync(
            fixture, McpCallerProtocol.Node1, key, deadline.Token));
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await McpUnauthorizedProbe.VerifyAsync(fixture, McpCallerProtocol.Node1, key, deadline.Token);
        await InitialCredentialAuthorityFlow.RunAsync(fixture, key, deadline.Token);
    }
}
