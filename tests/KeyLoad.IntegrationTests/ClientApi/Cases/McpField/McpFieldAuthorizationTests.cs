using KeyLoad.IntegrationTests.Features.Authorization;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>AC-MCP-002/007 and AC-DSTORE-003: both real adapters enforce the same persisted sensitive-field policy.</summary>
/// <param name="fixture">The initialized actual execution Aspire Orleans application.</param>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class McpFieldAuthorizationTests(ClusterFixture fixture)
{
    /// <summary>Scoped document grants permit projected reads while a missing field-write grant denies a real mutation.</summary>
    [Test]
    public async Task AcMcp002FieldProjectionAndDeniedPatchHaveTheSameCanonicalAuthorityAcrossClients()
    {
        using var deadline = McpCallerDeadline.Create();
        var state = await SensitiveAuthorityWarm.RunAsync(fixture, deadline.Token);
        await SensitiveAuthorityCold.RunAsync(fixture, state, deadline.Token);
    }
}
