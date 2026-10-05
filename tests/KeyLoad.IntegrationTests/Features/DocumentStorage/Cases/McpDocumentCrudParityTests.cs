using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.DocumentStorage;

/// <summary>AC-DSTORE-001: successful document CRUD has the same real RF3 outcome through both public clients.</summary>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class McpDocumentCrudParityTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcDstore001SdkPatchAndMcpDeleteMatchTheMirroredCrudLifecycle()
    {
        using var deadline = McpCallerDeadline.Create();
        var source = await McpDocumentScenario.CreateAsync(fixture, deadline.Token);
        var scenario = new McpDocumentCrudParityScenario(source.Partition);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, source.Partition,
            Capability.DocumentsRead | Capability.DocumentsWrite, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);

        await RunCrudLifecycleAsync(sdk, mcp, scenario, sdkCreates: true, deadline.Token);
    }

    [Test]
    public async Task AcDstore001McpPatchAndSdkDeleteMatchTheMirroredCrudLifecycle()
    {
        using var deadline = McpCallerDeadline.Create();
        var source = await McpDocumentScenario.CreateAsync(fixture, deadline.Token);
        var scenario = new McpDocumentCrudParityScenario(source.Partition);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, source.Partition,
            Capability.DocumentsRead | Capability.DocumentsWrite, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);

        await RunCrudLifecycleAsync(sdk, mcp, scenario, sdkCreates: false, deadline.Token);
    }

    [Test]
    public async Task AcDstore001StaleExplicitReplacementIsRejectedWithoutChangingRevisionTwo()
    {
        using var deadline = McpCallerDeadline.Create();
        var source = await McpDocumentScenario.CreateAsync(fixture, deadline.Token);
        var scenario = new McpDocumentCrudParityScenario(source.Partition);
        var identity = await McpPersistedIdentity.CreateAsync(fixture, source.Partition,
            Capability.DocumentsRead | Capability.DocumentsWrite, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            identity.Secret, deadline.Token);

        await CommitAndRetryAsync(sdk, mcp, scenario.CreateCommand(Guid.NewGuid()), sdkPrimary: true, deadline.Token);
        await CommitAndRetryAsync(sdk, mcp, scenario.ReplaceCommand(Guid.NewGuid()), sdkPrimary: true, deadline.Token);
        var staleCommand = scenario.StaleReplaceCommand(Guid.NewGuid());
        var rejected = await mcp.CallAsync(McpCallerTools.DocumentsCommit, staleCommand, deadline.Token);
        await McpCallerAssertions.ErrorAsync(rejected, ErrorCode.RevisionConflict, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(rejected, identity.Secret, McpDocumentCrudParityScenario.StaleJson);
        var sdkRejected = await sdk.CommitAsync(staleCommand, deadline.Token);
        await Assert.That(sdkRejected.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.RevisionConflict));
        await McpDocumentCrudParityAssertions.AssertDocumentAsync(sdk, mcp, scenario,
            McpDocumentCrudParityScenario.ReplacedRevision, McpDocumentCrudParityScenario.ReplacedJson, deadline.Token);
    }

    private static async Task RunCrudLifecycleAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        McpDocumentCrudParityScenario scenario, bool sdkCreates, CancellationToken cancellationToken)
    {
        await CommitAndVerifyAsync(sdk, mcp, scenario.CreateCommand(Guid.NewGuid()), "putDocument",
            McpDocumentCrudParityScenario.CreatedRevision, McpDocumentCrudParityScenario.CreatedJson,
            scenario, sdkCreates, cancellationToken);
        await CommitAndVerifyAsync(sdk, mcp, scenario.ReplaceCommand(Guid.NewGuid()), "putDocument",
            McpDocumentCrudParityScenario.ReplacedRevision, McpDocumentCrudParityScenario.ReplacedJson,
            scenario, !sdkCreates, cancellationToken);
        await CommitAndVerifyAsync(sdk, mcp, scenario.PatchCommand(Guid.NewGuid()), "patchDocument",
            McpDocumentCrudParityScenario.PatchedRevision, McpDocumentCrudParityScenario.PatchedJson,
            scenario, sdkCreates, cancellationToken);
        await CommitAndVerifyAsync(sdk, mcp, scenario.DeleteCommand(Guid.NewGuid()), "deleteDocument",
            McpDocumentCrudParityScenario.TombstoneRevision, null, scenario, !sdkCreates, cancellationToken);
        await McpDocumentCrudParityAssertions.AssertDocumentAsync(sdk, mcp, scenario, null, null, cancellationToken);
    }

    private static async Task CommitAndVerifyAsync(KeyLoadClient sdk, McpOfficialClient mcp, CommandRequest command,
        string kind, long revision, string? expectedJson, McpDocumentCrudParityScenario scenario, bool sdkPrimary,
        CancellationToken cancellationToken)
    {
        var receipt = await CommitAndRetryAsync(sdk, mcp, command, sdkPrimary, cancellationToken);
        await McpDocumentCrudParityAssertions.AssertMutationAsync(receipt, command.CommandId, kind, revision);
        long? visibleRevision = expectedJson is null ? null : revision;
        await McpDocumentCrudParityAssertions.AssertDocumentAsync(sdk, mcp, scenario, visibleRevision,
            expectedJson, cancellationToken);
    }

    private static Task<CommitReceipt> CommitAndRetryAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        CommandRequest command, bool sdkPrimary, CancellationToken cancellationToken)
        => McpDocumentCrudParityAssertions.CommitAndRetryAsync(sdk, mcp, command, sdkPrimary, cancellationToken);
}
