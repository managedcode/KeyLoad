using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.DocumentStorage;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

/// <summary>AC-CLIENT-004/005/006: SDK and official MCP dispatch commands share exact effects and replay.</summary>
/// <param name="fixture">The actual Aspire-owned Docker RF3 application.</param>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class AdminDispatchClientParityTests(ClusterFixture fixture)
{
    private const string FirstReplacementJson = "{\"value\":\"sdk-resumed\"}";
    private const string SecondReplacementJson = "{\"value\":\"mcp-resumed\"}";
    private const long SeedDocumentRevision = 1;
    private const long RevisionIncrement = 1;

    [Test]
    public async Task AcClient004SdkAndOfficialMcpDispatchControlsShareIdentityAndState()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await McpDocumentScenario.CreateAsync(fixture, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey, IntegrationClientOptions.Execution());
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            await AssertSdkDispatchAsync(sdk, Guid.NewGuid(), false, deadline.Token);
            await VerifyPersistedMemberCannotPauseAsync(fixture, scenario, deadline.Token);
            await scenario.SeedAsync(fixture, deadline.Token);
            var lane = await AdminDispatchQueueVerifier.SeedAsync(sdk, scenario.Partition, deadline.Token);
            await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
                fixture.AdminKey, deadline.Token);
            var tool = await mcp.Client.DiscoverKeyLoadToolAsync(McpCallerTools.AdminDispatch, deadline.Token);
            await McpDiscoveryAssertions.VerifyAsync(tool);
            await RunParityFlowAsync(sdk, mcp, scenario, lane, deadline.Token);
        }, failures);
        using var cleanupDeadline = new CancellationTokenSource(McpCallerProtocol.Deadline, TimeProvider.System);
        await ServerFailureObserver.ObserveAsync(() => RestoreDispatchAsync(sdk, cleanupDeadline.Token), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task VerifyPersistedMemberCannotPauseAsync(ClusterFixture fixture,
        McpDocumentScenario scenario, CancellationToken cancellationToken)
    {
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            Capability.DocumentsRead, cancellationToken);
        using var memberHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node3);
        var member = new KeyLoadClient(memberHttp, identity.Secret, IntegrationClientOptions.Execution());
        var denied = await member.SetDispatchAsync(Guid.NewGuid(), true, cancellationToken);
        await Assert.That(denied.IsFailed).IsTrue();
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
    }

    private static async Task RunParityFlowAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        McpDocumentScenario scenario, QueueLaneRef lane, CancellationToken cancellationToken)
    {
        var pauseId = Guid.NewGuid();
        await AssertSdkDispatchAsync(sdk, pauseId, true, cancellationToken);
        await AssertSdkDispatchAsync(sdk, pauseId, true, cancellationToken);
        await AssertMcpDispatchAsync(mcp, pauseId, true, cancellationToken);
        var conflict = await SetMcpDispatchAsync(mcp, pauseId, false, cancellationToken);
        await McpCallerAssertions.ErrorAsync(conflict, ErrorCode.Conflict, dispatched: true);
        await AdminDispatchQueueVerifier.AssertPausedAsync(sdk, mcp, lane, cancellationToken);
        await AssertDocumentAsync(sdk, scenario, McpDocumentProtocol.InitialJson, SeedDocumentRevision, cancellationToken);
        await AssertMcpDispatchAsync(mcp, Guid.NewGuid(), false, cancellationToken);
        await AdminDispatchQueueVerifier.DeliverAndAcknowledgeAsync(sdk, mcp, lane, cancellationToken);
        await AssertDocumentWriteAsync(sdk, scenario, FirstReplacementJson, SeedDocumentRevision, cancellationToken);
        await AssertMcpDispatchAsync(mcp, Guid.NewGuid(), true, cancellationToken);
        await AdminDispatchQueueVerifier.AssertPausedAsync(sdk, mcp, lane, cancellationToken);
        await AssertDocumentAsync(sdk, scenario, FirstReplacementJson, SeedDocumentRevision + RevisionIncrement, cancellationToken);
        await AssertSdkDispatchAsync(sdk, Guid.NewGuid(), false, cancellationToken);
        await AssertDocumentWriteAsync(sdk, scenario, SecondReplacementJson,
            SeedDocumentRevision + RevisionIncrement, cancellationToken);
        await AssertDocumentAsync(sdk, scenario, SecondReplacementJson,
            SeedDocumentRevision + RevisionIncrement * 2, cancellationToken);
    }

    private static async Task AssertSdkDispatchAsync(KeyLoadClient sdk, Guid commandId, bool paused,
        CancellationToken cancellationToken)
    {
        var result = await McpCallerAssertions.SdkSuccessAsync(await sdk.SetDispatchAsync(commandId, paused, cancellationToken));
        await Assert.That(result).IsTrue();
    }

    private static async Task AssertMcpDispatchAsync(McpOfficialClient mcp, Guid commandId, bool paused,
        CancellationToken cancellationToken)
    {
        var reply = await SetMcpDispatchAsync(mcp, commandId, paused, cancellationToken);
        var result = await McpCallerAssertions.SuccessAsync<bool>(reply);
        await Assert.That(result.Value).IsTrue();
    }

    private static Task<ModelContextProtocol.Protocol.CallToolResult> SetMcpDispatchAsync(McpOfficialClient mcp,
        Guid commandId, bool paused, CancellationToken cancellationToken)
    {
        var arguments = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [McpCallerProtocol.CommandId] = commandId,
            [McpCallerProtocol.Request] = paused
        };
        return mcp.Client.InvokeKeyLoadToolAsync(McpCallerTools.AdminDispatch, arguments, cancellationToken).AsTask();
    }

    private static async Task AssertDocumentWriteAsync(KeyLoadClient sdk, McpDocumentScenario scenario,
        string json, long revision, CancellationToken cancellationToken)
    {
        await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(Update(scenario, json, revision), cancellationToken));
        await AssertDocumentAsync(sdk, scenario, json, revision + RevisionIncrement, cancellationToken);
    }

    private static CommandRequest Update(McpDocumentScenario scenario, string json, long revision)
        => new(Guid.NewGuid(), scenario.Partition,
            [new PutDocument(McpDocumentProtocol.Collection, McpDocumentProtocol.Entity, json, revision)]);

    private static async Task AssertDocumentAsync(KeyLoadClient sdk, McpDocumentScenario scenario, string json,
        long revision, CancellationToken cancellationToken)
    {
        var document = await McpCallerAssertions.SdkSuccessAsync(await sdk.GetAsync(scenario.Reference, cancellationToken));
        await Assert.That(document!.Json).IsEqualTo(json);
        await Assert.That(document.Revision).IsEqualTo(revision);
    }

    private static async Task RestoreDispatchAsync(KeyLoadClient sdk, CancellationToken cancellationToken)
    {
        var restored = await McpCallerAssertions.SdkSuccessAsync(await sdk.SetDispatchAsync(Guid.NewGuid(), false, cancellationToken));
        await Assert.That(restored).IsTrue();
    }
}
