using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

/// <summary>AC-MCP-002/007 and AC-DSTORE-003: both real adapters enforce the same persisted sensitive-field policy.</summary>
/// <param name="fixture">The initialized actual Docker/Aspire RF3 application.</param>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class McpFieldAuthorizationTests(ClusterFixture fixture)
{
    /// <summary>Scoped document grants permit projected reads while a missing field-write grant denies a real mutation.</summary>
    [Test]
    public async Task AcMcp002FieldProjectionAndDeniedPatchHaveTheSameCanonicalAuthorityAcrossClients()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await McpDocumentScenario.CreateProtectedAsync(fixture, deadline.Token);
        await scenario.SeedAsync(fixture, deadline.Token);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            Capability.DocumentsRead | Capability.DocumentsWrite, deadline.Token);
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            identity.Secret, deadline.Token);
        var before = await McpCallerAssertions.SuccessAsync<DocumentResult>(await session.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(scenario.Reference), deadline.Token));
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node3);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        var httpBefore = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(scenario.Reference, deadline.Token));
        await Assert.That(before.Value.Redacted).IsTrue();
        await Assert.That(before.Value.RedactedFields).Contains(McpDocumentProtocol.ProtectedPath);
        await Assert.That(before.Value.Json.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal)).IsFalse();
        await Assert.That(JsonDefaults.Serialize(before.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(httpBefore))).IsTrue();
        var deniedCommand = new CommandRequest(Guid.NewGuid(), scenario.Partition,
            [new PatchDocument(McpDocumentProtocol.Collection, McpDocumentProtocol.Entity,
                [new(McpDocumentProtocol.ProtectedPath, PatchKind.Set, McpDocumentProtocol.ProtectedPatchJson)], McpCallerProtocol.FirstRevision)]);
        var denied = await session.CallAsync(McpCallerTools.DocumentsCommit, deniedCommand, deadline.Token);
        await McpCallerAssertions.ErrorAsync(denied, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(denied, identity.Secret, McpDocumentProtocol.ConflictValue);
        var httpDenied = await sdk.CommitAsync(deniedCommand, deadline.Token);
        await Assert.That(httpDenied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        var after = await McpCallerAssertions.SuccessAsync<DocumentResult>(await session.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(scenario.Reference), deadline.Token));
        await Assert.That(JsonDefaults.Serialize(before.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(after.Value))).IsTrue();
        var original = await McpCallerAssertions.SdkSuccessAsync(await new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution())
            .GetAsync(scenario.Reference, deadline.Token));
        await Assert.That(original!.Revision).IsEqualTo(McpCallerProtocol.FirstRevision);
        await Assert.That(original.Json.Contains(McpDocumentProtocol.PrivateValue, StringComparison.Ordinal)).IsTrue();
    }
}
