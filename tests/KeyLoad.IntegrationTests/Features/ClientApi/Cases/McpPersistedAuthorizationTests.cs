using KeyLoad.IntegrationTests.Features.Authorization;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>AC-MCP-002/003: persisted grants and server scope govern each actual official tool execution.</summary>
/// <param name="fixture">The initialized actual Docker/Aspire RF3 application.</param>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class McpPersistedAuthorizationTests(ClusterFixture fixture)
{
    /// <summary>Changing grants after discovery changes the next execution without capturing session authority.</summary>
    [Test]
    public async Task AcMcp002CurrentPersistedPrincipalControlsEachCallAfterDiscovery()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await McpDocumentScenario.CreateAsync(fixture, deadline.Token);
        await scenario.SeedAsync(fixture, deadline.Token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition, Capability.DocumentsRead, deadline.Token);
        var state = new DiscoveryAuthorityState(scenario, identity, Guid.NewGuid(), identity.Principal with
        {
            PolicyEpoch = DiscoveryAuthorityProtocol.FinalEpoch
        });
        await DiscoveryAuthorityFlow.RunAsync(fixture, state, deadline.Token);
    }

    /// <summary>A valid key cannot read a different real tenant or provide trusted authority in tool arguments.</summary>
    [Test]
    public async Task AcMcp002CrossTenantAndForgedAuthorityAreDeniedWithoutChangingTheDocument()
    {
        using var deadline = McpCallerDeadline.Create();
        var state = await AdversarialAuthorityTrial.WarmAsync(fixture, deadline.Token);
        await AdversarialAuthorityCold.RunAsync(fixture, state, deadline.Token);
    }
}
