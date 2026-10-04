using System.Net;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>AC-MCP-002: real native callers cannot retain authority from an earlier discovery request.</summary>
/// <param name="fixture">The initialized actual Docker/Aspire RF3 application.</param>
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
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            identity.Secret, deadline.Token);
        await session.Client.ListToolsAsync(cancellationToken: deadline.Token);
        await McpCallerAssertions.SuccessAsync<DocumentResult>(await session.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(scenario.Reference), deadline.Token));

        var unusable = expire ? identity.Credential with { ExpiresAt = TimeProvider.System.GetUtcNow() - McpCallerProtocol.ExpiredOffset }
            : identity.Credential with { Revoked = true };
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(http, fixture.AdminKey);
        await McpCallerAssertions.SdkSuccessAsync(await administrator.ConfigureApiKeyAsync(Guid.NewGuid(), unusable, deadline.Token));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() => session.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(scenario.Reference), deadline.Token));
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(error!.Message.Contains(identity.Secret, StringComparison.Ordinal)).IsFalse();
        await McpUnauthorizedProbe.VerifyAsync(fixture, McpCallerProtocol.Node2, identity.Secret, deadline.Token);
        var rejected = await new KeyLoadClient(http, identity.Secret).GetAsync(scenario.Reference, deadline.Token);
        await Assert.That(rejected.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Unauthenticated));
        await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await administrator.GetAsync(scenario.Reference, deadline.Token)))!.Revision)
            .IsEqualTo(McpCallerProtocol.FirstRevision);
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
    }
}
