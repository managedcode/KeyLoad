using System.Net;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>AC-MCPGW-007: official resource and prompt calls remain tied to fresh persisted identity.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class McpAgentGuideRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task OfficialGuideAndPromptAreBoundedAndRevokedCredentialsAreRejected()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await McpDocumentScenario.CreateAsync(fixture, deadline.Token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            Capability.DocumentsRead, deadline.Token);
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            identity.Secret, deadline.Token);
        await McpAgentGuideRf3Assertions.VerifyGuideAsync(session, deadline.Token);
        await McpAgentGuideRf3Assertions.VerifyRejectedInputsAsync(session, deadline.Token);
        await RevokeAndVerifyAsync(identity, session, deadline.Token);
    }

    private async Task RevokeAndVerifyAsync(McpPersistedIdentity identity, McpOfficialClient session,
        CancellationToken cancellationToken)
    {
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey);
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureApiKeyAsync(Guid.NewGuid(),
            identity.Credential with { Revoked = true }, cancellationToken));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            session.Client.ListResourcesAsync(cancellationToken: cancellationToken).AsTask());
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(error.Message.Contains(identity.Secret, StringComparison.Ordinal)).IsFalse();
    }
}
