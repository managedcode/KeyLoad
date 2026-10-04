using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

/// <summary>AC-MCP-002/005/007 and AC-EVENT-004/005/006 real RF3 EventStreams parity.</summary>
/// <param name="fixture">The initialized actual Docker/Aspire RF3 application.</param>
[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class McpEventStreamTests(ClusterFixture fixture)
{
    /// <summary>A public SDK append and official MCP stable retry produce one canonical ordered stream.</summary>
    [Test]
    public async Task AcMcp005SdkAppendAndMcpRetryReturnOneCommittedEventBatch()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await McpEventStreamScenario.CreateAsync(fixture, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey);
        var command = scenario.AppendCommand(Guid.NewGuid());
        var original = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(command, deadline.Token));
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node2,
            fixture.AdminKey, deadline.Token);

        var retry = await McpCallerAssertions.SuccessAsync<CommitReceipt>(await session.CallAsync(
            McpCallerTools.DocumentsCommit, command, deadline.Token));
        await Assert.That(retry.Value.CommandId).IsEqualTo(command.CommandId);
        await Assert.That(retry.Value.Token).IsEqualTo(original.Token);
        await Assert.That(JsonDefaults.Serialize(retry.Value).AsSpan().SequenceEqual(JsonDefaults.Serialize(original))).IsTrue();
        var sdkPage = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadStreamAsync(
            scenario.ReadRequest(McpEventStreamTokens.InitialRevision, McpEventStreamTokens.ExpectedEventCount), deadline.Token));
        var mcpPage = await McpCallerAssertions.SuccessAsync<StreamPage>(await session.CallAsync(
            McpCallerTools.StreamsRead,
            scenario.ReadRequest(McpEventStreamTokens.InitialRevision, McpEventStreamTokens.ExpectedEventCount), deadline.Token));
        await McpEventStreamAssertions.EqualPageAsync(scenario, sdkPage, mcpPage.Value,
            McpEventStreamScenario.ExpectedEvents.ToArray(), hasMore: false, appendReceiptPosition: original.Token.Position,
            previousCutPosition: McpEventStreamTokens.InitialCutPosition);
        await Assert.That(mcpPage.RequestId).IsNotEqualTo(retry.RequestId);
    }

    /// <summary>Bounded first page, exclusive continuation and empty tail are identical through both public clients.</summary>
    [Test]
    public async Task AcMcp007SdkAndOfficialClientReturnIdenticalBoundedReplayPages()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await McpEventStreamScenario.CreateAsync(fixture, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, fixture.AdminKey);
        var appendReceipt = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(
            scenario.AppendCommand(Guid.NewGuid()), deadline.Token));
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            fixture.AdminKey, deadline.Token);

        var firstRequest = scenario.ReadRequest(McpEventStreamTokens.InitialRevision, McpEventStreamTokens.FirstPageLimit);
        var firstSdk = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadStreamAsync(firstRequest, deadline.Token));
        var firstMcp = await McpCallerAssertions.SuccessAsync<StreamPage>(await session.CallAsync(
            McpCallerTools.StreamsRead, firstRequest, deadline.Token));
        var lastCut = await McpEventStreamAssertions.EqualPageAsync(scenario, firstSdk, firstMcp.Value,
            McpEventStreamScenario.ExpectedEvents.Take(McpEventStreamTokens.ExpectedFirstPageCount).ToArray(), hasMore: true,
            appendReceiptPosition: appendReceipt.Token.Position,
            previousCutPosition: McpEventStreamTokens.InitialCutPosition);
        await Assert.That(firstMcp.Value.Events.Length).IsEqualTo(McpEventStreamTokens.ExpectedFirstPageCount);
        await Assert.That(firstMcp.Value.Events.Select(record => record.EventSequence).SequenceEqual(
            new[] { McpEventStreamTokens.FirstExpectedSequence, McpEventStreamTokens.SecondExpectedSequence })).IsTrue();

        var continuation = firstMcp.Value.Events[^1].Revision;
        await Assert.That(continuation).IsEqualTo(McpEventStreamTokens.SecondEventRevision);
        var secondRequest = scenario.ReadRequest(continuation, McpEventStreamTokens.FirstPageLimit);
        var secondSdk = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadStreamAsync(secondRequest, deadline.Token));
        var secondMcp = await McpCallerAssertions.SuccessAsync<StreamPage>(await session.CallAsync(
            McpCallerTools.StreamsRead, secondRequest, deadline.Token));
        lastCut = await McpEventStreamAssertions.EqualPageAsync(scenario, secondSdk, secondMcp.Value,
            McpEventStreamScenario.ExpectedEvents.Skip(McpEventStreamTokens.ExpectedFirstPageCount).ToArray(), hasMore: false,
            appendReceiptPosition: appendReceipt.Token.Position, previousCutPosition: lastCut);
        await Assert.That(secondMcp.Value.Events.Length).IsEqualTo(McpEventStreamTokens.ExpectedSecondPageCount);
        await Assert.That(secondMcp.Value.Events[0].Revision).IsEqualTo(McpEventStreamTokens.LastEventRevision);
        await Assert.That(secondMcp.Value.Events[0].EventSequence).IsEqualTo(McpEventStreamTokens.ThirdExpectedSequence);

        var tailRequest = scenario.ReadRequest(McpEventStreamTokens.LastEventRevision, McpEventStreamTokens.EmptyTailLimit);
        var tailSdk = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadStreamAsync(tailRequest, deadline.Token));
        var tailMcp = await McpCallerAssertions.SuccessAsync<StreamPage>(await session.CallAsync(
            McpCallerTools.StreamsRead, tailRequest, deadline.Token));
        await McpEventStreamAssertions.EqualPageAsync(scenario, tailSdk, tailMcp.Value, [], hasMore: false,
            appendReceiptPosition: appendReceipt.Token.Position, previousCutPosition: lastCut);
        await Assert.That(tailMcp.Value.Events).IsEmpty();
        await Assert.That(new[] { firstMcp.RequestId, secondMcp.RequestId, tailMcp.RequestId }.Distinct().Count())
            .IsEqualTo(McpEventStreamTokens.ExpectedPageCallCount);
    }

    /// <summary>Persisted stream-set grants, tenant isolation and stale generations fail safely and preserve later reads.</summary>
    [Test]
    public async Task AcMcp002ScopedEventReadDeniesForeignTenantAndStaleGenerationWithoutLeakingData()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await McpEventStreamScenario.CreateAsync(fixture, deadline.Token);
        using var administratorHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var administrator = new KeyLoadClient(administratorHttp, fixture.AdminKey);
        var appendReceipt = await McpCallerAssertions.SdkSuccessAsync(await administrator.CommitAsync(
            scenario.AppendCommand(Guid.NewGuid()), deadline.Token));
        var identity = await McpPersistedIdentity.CreateAsync(fixture, scenario.Partition,
            McpEventStreamTokens.StreamSet, Capability.EventsRead, deadline.Token);
        using var readerHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var sdk = new KeyLoadClient(readerHttp, identity.Secret);
        await using var session = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            identity.Secret, deadline.Token);

        var healthyRequest = scenario.ReadRequest(McpEventStreamTokens.InitialRevision, McpEventStreamTokens.ExpectedEventCount);
        var initialSdk = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadStreamAsync(healthyRequest, deadline.Token));
        var initialMcp = await McpCallerAssertions.SuccessAsync<StreamPage>(await session.CallAsync(
            McpCallerTools.StreamsRead, healthyRequest, deadline.Token));
        var lastCut = await McpEventStreamAssertions.EqualPageAsync(scenario, initialSdk, initialMcp.Value,
            McpEventStreamScenario.ExpectedEvents.ToArray(), hasMore: false, appendReceiptPosition: appendReceipt.Token.Position,
            previousCutPosition: McpEventStreamTokens.InitialCutPosition);
        var foreignId = await VerifyForeignTenantDenialAsync(sdk, session, healthyRequest, identity, deadline.Token);
        var invalidLimitId = await VerifyInvalidLimitAsync(sdk, session, scenario, identity, deadline.Token);
        var staleId = await VerifyStaleGenerationAsync(sdk, session, scenario, identity, deadline.Token);

        var healthySdk = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadStreamAsync(healthyRequest, deadline.Token));
        var healthyMcp = await McpCallerAssertions.SuccessAsync<StreamPage>(await session.CallAsync(
            McpCallerTools.StreamsRead, healthyRequest, deadline.Token));
        await McpEventStreamAssertions.EqualPageAsync(scenario, healthySdk, healthyMcp.Value,
            McpEventStreamScenario.ExpectedEvents.ToArray(), hasMore: false, appendReceiptPosition: appendReceipt.Token.Position,
            previousCutPosition: lastCut);
        await Assert.That(new[] { initialMcp.RequestId, foreignId, invalidLimitId, staleId, healthyMcp.RequestId }.Distinct().Count())
            .IsEqualTo(McpEventStreamTokens.ExpectedProtectedCallCount);
    }

    private static async Task<Guid?> VerifyForeignTenantDenialAsync(KeyLoadClient sdk, McpOfficialClient session,
        ReadStreamRequest request, McpPersistedIdentity identity, CancellationToken cancellationToken)
    {
        var foreign = request with
        {
            Stream = request.Stream with
            { Partition = request.Stream.Partition with { TenantId = McpEventStreamTokens.ForeignTenant } }
        };
        var mcp = await session.CallAsync(McpCallerTools.StreamsRead, foreign, cancellationToken);
        var requestId = await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.PermissionDenied, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(mcp, identity.Secret, McpEventStreamTokens.PrivateMarker);
        var http = await sdk.ReadStreamAsync(foreign, cancellationToken);
        await Assert.That(http.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.PermissionDenied));
        await McpEventStreamAssertions.DoesNotDiscloseProblemAsync(http.Problem, identity.Secret,
            McpEventStreamTokens.PrivateMarker);
        return requestId;
    }

    private static async Task<Guid?> VerifyInvalidLimitAsync(KeyLoadClient sdk, McpOfficialClient session,
        McpEventStreamScenario scenario, McpPersistedIdentity identity, CancellationToken cancellationToken)
    {
        var invalid = scenario.ReadRequest(McpEventStreamTokens.InitialRevision, McpEventStreamTokens.InvalidPageLimit);
        var mcp = await session.CallAsync(McpCallerTools.StreamsRead, invalid, cancellationToken);
        var requestId = await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.BudgetExceeded, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(mcp, identity.Secret, McpEventStreamTokens.PrivateMarker);
        var http = await sdk.ReadStreamAsync(invalid, cancellationToken);
        await Assert.That(http.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.BudgetExceeded));
        await McpEventStreamAssertions.DoesNotDiscloseProblemAsync(http.Problem, identity.Secret,
            McpEventStreamTokens.PrivateMarker);
        return requestId;
    }

    private static async Task<Guid?> VerifyStaleGenerationAsync(KeyLoadClient sdk, McpOfficialClient session,
        McpEventStreamScenario scenario, McpPersistedIdentity identity, CancellationToken cancellationToken)
    {
        var stale = scenario.ReadRequest(McpEventStreamTokens.InitialRevision,
            McpEventStreamTokens.ExpectedEventCount, McpEventStreamTokens.StaleGeneration);
        var mcp = await session.CallAsync(McpCallerTools.StreamsRead, stale, cancellationToken);
        var requestId = await McpCallerAssertions.ErrorAsync(mcp, ErrorCode.TokenInvalidated, dispatched: true);
        await McpCallerAssertions.DoesNotDiscloseAsync(mcp, identity.Secret, McpEventStreamTokens.PrivateMarker);
        var http = await sdk.ReadStreamAsync(stale, cancellationToken);
        await Assert.That(http.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.TokenInvalidated));
        await McpEventStreamAssertions.DoesNotDiscloseProblemAsync(http.Problem, identity.Secret,
            McpEventStreamTokens.PrivateMarker);
        return requestId;
    }
}
