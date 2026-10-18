using System.Net;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class AggregateReplayClusterTests(ClusterFixture fixture)
{
    [Test]
    public async Task AcEvent007010SdkAndOfficialMcpMatchSnapshotTailReferenceAndRejectInvalidSlices()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await AggregateReplayClusterScenario.CreateAsync(fixture, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, scenario.WorkerSecret, IntegrationClientOptions.Execution());
        var append = await scenario.AppendInitialAsync(sdk, Guid.NewGuid(), deadline.Token);
        await Assert.That(append.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        var snapshot = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(
            scenario.InitialSnapshotCommand(Guid.NewGuid()), deadline.Token));
        await Assert.That(snapshot.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(snapshot.Mutations.Single().Revision).IsEqualTo(AggregateReplayClusterTokens.FirstSnapshotVersion);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            scenario.WorkerSecret, deadline.Token);
        var request = scenario.ReadRequest();
        var sdkPage = McpCallerAssertions.SdkSuccessAsync(await sdk.ReadAggregateReplayAsync(request, deadline.Token));
        var mcpPage = McpCallerAssertions.SuccessAsync<AggregateReplayPage>(await mcp.CallAsync(
            McpCallerTools.StreamsReplay, request, deadline.Token));
        await AggregateReplayClusterAssertions.SamePageAsync(await sdkPage, (await mcpPage).Value,
            expectedCount: AggregateReplayClusterTokens.InitialReducedCount,
            expectedTailCount: AggregateReplayClusterTokens.TailCount,
            expectedEventIds: [AggregateReplayClusterTokens.PaidEventId, AggregateReplayClusterTokens.ShippedEventId],
            expectedSnapshotState: AggregateReplayClusterTokens.InitialState);

        await AggregateReplayClusterAssertions.ErrorAsync(sdk, mcp,
            request with
            {
                ReducerVersion = AggregateReplayClusterTokens.UnknownReducer
            },
            ErrorCode.FormatUnsupported, deadline.Token);
        await AggregateReplayClusterAssertions.ErrorAsync(sdk, mcp,
            request with
            {
                StateSchemaVersion = AggregateReplayClusterTokens.UnknownStateSchema
            },
            ErrorCode.FormatUnsupported, deadline.Token);
        await AggregateReplayClusterAssertions.ErrorAsync(sdk, mcp,
            request with
            {
                Stream = request.Stream with
                {
                    Generation = AggregateReplayClusterTokens.Generation + 1
                }
            },
            ErrorCode.TokenInvalidated, deadline.Token);
        await AggregateReplayClusterAssertions.ErrorAsync(sdk, mcp,
            request with
            {
                MaximumEvents = AggregateReplayClusterTokens.TooSmallTail
            },
            ErrorCode.BudgetExceeded, deadline.Token);
    }

    [Test]
    public async Task AcEvent009PersistedRawGrantsAuthorizeExactInputsAndRevocationDeniesBothClients()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await AggregateReplayClusterScenario.CreateAsync(fixture, deadline.Token);
        await AggregateReplayAuthorizationContinuation.RunAsync(fixture, scenario, this, deadline.Token);
    }

    internal async Task AssertRevokedPrincipalDeniedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        ReadAggregateReplayRequest request, string secret, CancellationToken cancellationToken)
    {
        var sdkResult = await sdk.ReadAggregateReplayAsync(request, cancellationToken);
        await Assert.That(sdkResult.IsSuccess).IsFalse();
        await Assert.That(sdkResult.Value).IsNull();
        await Assert.That(sdkResult.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Unauthenticated));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            mcp.CallAsync(McpCallerTools.StreamsReplay, request, cancellationToken));
        await Assert.That(error!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(error.Message.Contains(secret, StringComparison.Ordinal)).IsFalse();
        await McpUnauthorizedProbe.VerifyAsync(fixture, McpCallerProtocol.Node3, secret, cancellationToken);
    }

    internal async Task<AggregateReplayAuthorizationOriginal> VerifyPersistedRawGrantsAsync(
        AggregateReplayClusterScenario scenario, KeyLoadClient sdk, McpOfficialClient mcp,
        CancellationToken cancellationToken)
    {
        var append = await scenario.AppendInitialAsync(sdk, Guid.NewGuid(), cancellationToken);
        await Assert.That(append.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        var snapshotCommand = scenario.InitialSnapshotCommand(Guid.NewGuid());
        var snapshot = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(
            snapshotCommand, cancellationToken));
        await Assert.That(snapshot.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        var request = scenario.ReadRequest();
        var sdkPage = McpCallerAssertions.SdkSuccessAsync(await sdk.ReadAggregateReplayAsync(request, cancellationToken));
        var mcpPage = McpCallerAssertions.SuccessAsync<AggregateReplayPage>(await mcp.CallAsync(
            McpCallerTools.StreamsReplay, request, cancellationToken));
        var sdkValue = await sdkPage;
        var mcpValue = (await mcpPage).Value;
        await AggregateReplayClusterAssertions.SamePageAsync(sdkValue, mcpValue,
            AggregateReplayClusterTokens.InitialReducedCount, AggregateReplayClusterTokens.TailCount,
            [AggregateReplayClusterTokens.PaidEventId, AggregateReplayClusterTokens.ShippedEventId],
            AggregateReplayClusterTokens.InitialState);
        using var payload = JsonDocument.Parse(sdkValue.Events[0].Data.PayloadJson);
        using var headers = JsonDocument.Parse(sdkValue.Events[0].Data.HeadersJson);
        await Assert.That(payload.RootElement.GetProperty(AggregateReplayClusterTokens.SecretProperty).GetString())
            .IsEqualTo(AggregateReplayClusterTokens.Secret);
        await Assert.That(headers.RootElement.GetProperty(AggregateReplayClusterTokens.PrivateHeaderProperty).GetString())
            .IsEqualTo(AggregateReplayClusterTokens.PrivateHeader);
        var restricted = await scenario.CreateMissingGrantWorkerAsync(fixture, cancellationToken);
        using var restrictedHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var restrictedSdk = new KeyLoadClient(restrictedHttp, restricted.Secret, IntegrationClientOptions.Execution());
        await using var restrictedMcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            restricted.Secret, cancellationToken);
        var denied = await AggregateReplayClusterAssertions.ErrorAsync(restrictedSdk, restrictedMcp,
            request, ErrorCode.PermissionDenied, cancellationToken);
        await McpCallerAssertions.DoesNotDiscloseAsync(denied, restricted.Secret, AggregateReplayClusterTokens.Secret);
        await McpCallerAssertions.DoesNotDiscloseAsync(denied, restricted.Secret, AggregateReplayClusterTokens.PrivateHeader);
        return new(request, snapshotCommand, snapshot, sdkValue);
    }
}
