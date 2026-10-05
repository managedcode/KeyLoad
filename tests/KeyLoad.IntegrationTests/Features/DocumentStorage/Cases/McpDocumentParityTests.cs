using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>AC-MCP-005/007 and AC-DSTORE-001: real RF3 MCP and HTTP clients share canonical document outcomes.</summary>
/// <param name="fixture">The initialized actual Docker/Aspire RF3 application.</param>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class McpDocumentParityTests(ClusterFixture fixture)
{
    /// <summary>Identical MCP and HTTP command retries retain one receipt and fresh execution identities.</summary>
    [Test]
    public async Task AcMcp005StableDocumentCommandHasTheSameOutcomeAcrossBothRealClients()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await McpDocumentScenario.CreateAsync(fixture, deadline.Token);
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            fixture.AdminKey, deadline.Token);
        var command = scenario.Command(Guid.NewGuid());
        var first = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await session.CallAsync(McpCallerTools.DocumentsCommit,
            command, deadline.Token));
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node3);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var httpRetry = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, deadline.Token));
        var nativeRetry = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await session.CallAsync(McpCallerTools.DocumentsCommit,
            command, deadline.Token));
        await Assert.That(first.Value.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(first.Value.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(httpRetry.Token).IsEqualTo(first.Value.Token);
        await Assert.That(nativeRetry.Value.Token).IsEqualTo(first.Value.Token);
        await Assert.That(JsonDefaults.Serialize(nativeRetry.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(httpRetry))).IsTrue();
        await Assert.That(first.RequestId).IsNotEqualTo(nativeRetry.RequestId);
        var document = await McpCallerAssertions.SuccessAsync<DocumentResult>(await session.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(scenario.Reference), deadline.Token));
        var httpDocument = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(scenario.Reference, deadline.Token));
        await Assert.That(document.Value.Revision).IsEqualTo(McpCallerProtocol.FirstRevision);
        await Assert.That(JsonDefaults.Serialize(document.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(httpDocument))).IsTrue();
        await Assert.That(new[] { first.RequestId, nativeRetry.RequestId, document.RequestId }.Distinct().Count())
            .IsEqualTo(McpDocumentProtocol.ObservedOperationCount);
    }

    /// <summary>Reusing an accepted stable ID with changed document content fails safely in both adapters without another revision.</summary>
    [Test]
    public async Task AcMcp005ConflictingCommandIsRejectedByBothClientsWithoutChangingTheDocument()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await McpDocumentScenario.CreateAsync(fixture, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var command = scenario.Command(Guid.NewGuid());
        var committed = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, deadline.Token));
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            fixture.AdminKey, deadline.Token);
        var changed = scenario.Command(command.CommandId, McpDocumentProtocol.ConflictingJson);
        var rejected = await session.CallAsync(McpCallerTools.DocumentsCommit, changed, deadline.Token);
        var rejectedId = await McpCallerAssertions.ErrorAsync(rejected, ErrorCode.Conflict, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(rejected, fixture.AdminKey, McpDocumentProtocol.ConflictValue);
        var httpRejected = await sdk.CommitAsync(changed, deadline.Token);
        await Assert.That(httpRejected.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Conflict));
        var read = await McpCallerAssertions.SuccessAsync<DocumentResult>(await session.CallAsync(McpCallerTools.DocumentsGet,
            new GetDocumentRequest(scenario.Reference), deadline.Token));
        var httpRead = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(scenario.Reference, deadline.Token));
        await Assert.That(read.Value.Revision).IsEqualTo(McpCallerProtocol.FirstRevision);
        await Assert.That(JsonDefaults.Serialize(read.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(httpRead))).IsTrue();
        await Assert.That(read.Value.Json.Contains(McpDocumentProtocol.ConflictValue, StringComparison.Ordinal)).IsFalse();
        await Assert.That(read.RequestId).IsNotEqualTo(rejectedId);
        await Assert.That((await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, deadline.Token))).Token)
            .IsEqualTo(committed.Token);
    }

    /// <summary>An absent document returns canonical null from the native and HTTP SDKs rather than a fabricated row.</summary>
    [Test]
    public async Task AcMcp007MissingDocumentIsCanonicalNullWithFreshNativeExecutionIds()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await McpDocumentScenario.CreateAsync(fixture, deadline.Token);
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            fixture.AdminKey, deadline.Token);
        var request = new GetDocumentRequest(scenario.Reference);
        var first = await McpCallerAssertions.SuccessAsync<DocumentResult?>(await session.CallAsync(McpCallerTools.DocumentsGet, request, deadline.Token));
        var second = await McpCallerAssertions.SuccessAsync<DocumentResult?>(await session.CallAsync(McpCallerTools.DocumentsGet, request, deadline.Token));
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node3);
        var missing = await McpCallerAssertions.SdkSuccessAsync(await new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution()).GetAsync(scenario.Reference, deadline.Token));
        await Assert.That(first.Value).IsNull();
        await Assert.That(second.Value).IsNull();
        await Assert.That(missing).IsNull();
        await Assert.That(first.RequestId).IsNotEqualTo(second.RequestId);
    }
}
