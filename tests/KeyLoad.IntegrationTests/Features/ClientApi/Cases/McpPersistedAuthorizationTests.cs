using KeyLoad.Client;
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
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            identity.Secret, deadline.Token);
        await session.Client.ListToolsAsync(cancellationToken: deadline.Token);
        var first = await McpCallerAssertions.SuccessAsync<DocumentResult>(await session.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(scenario.Reference), deadline.Token));
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey);
        var restricted = identity.Principal with { Grants = [], PolicyEpoch = identity.Principal.PolicyEpoch + McpCallerProtocol.EpochIncrement };
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigurePrincipalAsync(Guid.NewGuid(), restricted, deadline.Token));

        var denied = await session.CallAsync(McpCallerTools.DocumentsGet, new GetDocumentRequest(scenario.Reference), deadline.Token);
        var deniedId = await McpCallerAssertions.ErrorAsync(denied, ErrorCode.PermissionDenied, dispatched: true);
        await Assert.That(deniedId).IsNotEqualTo(first.RequestId);
        await McpCallerAssertions.DoesNotDiscloseAsync(denied, identity.Secret, McpDocumentProtocol.PrivateValue);
        var sdkDenied = await new KeyLoadClient(http, identity.Secret).GetAsync(scenario.Reference, deadline.Token);
        await Assert.That(sdkDenied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
    }

    /// <summary>A valid key cannot read a different real tenant or provide trusted authority in tool arguments.</summary>
    [Test]
    public async Task AcMcp002CrossTenantAndForgedAuthorityAreDeniedWithoutChangingTheDocument()
    {
        using var deadline = McpCallerDeadline.Create();
        var owned = await McpDocumentScenario.CreateAsync(fixture, deadline.Token);
        var foreign = await McpDocumentScenario.CreateAsync(fixture, deadline.Token);
        await owned.SeedAsync(fixture, deadline.Token);
        await foreign.SeedAsync(fixture, deadline.Token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, owned.Partition, Capability.DocumentsRead, deadline.Token);
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);
        var request = new GetDocumentRequest(foreign.Reference);
        var denied = await session.CallAsync(McpCallerTools.DocumentsGet, request, deadline.Token);
        await McpCallerAssertions.ErrorAsync(denied, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(denied, identity.Secret, McpDocumentProtocol.PrivateValue);
        var forged = McpOfficialClient.Arguments(request);
        forged.Add(McpCallerProtocol.PrincipalId, identity.Principal.Id);
        forged.Add(McpCallerProtocol.Roles, new[] { McpCallerProtocol.AdministratorRole });
        var rejected = await session.Client.CallToolAsync(McpCallerTools.DocumentsGet, forged, cancellationToken: deadline.Token);
        await McpCallerAssertions.ErrorAsync(rejected, ErrorCode.Validation, dispatched: false);
        var authorized = await McpCallerAssertions.SuccessAsync<DocumentResult>(await session.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(owned.Reference), deadline.Token));
        await Assert.That(authorized.Value.Reference).IsEqualTo(owned.Reference);
        await Assert.That(authorized.Value.Revision).IsEqualTo(McpCallerProtocol.FirstRevision);
    }
}
