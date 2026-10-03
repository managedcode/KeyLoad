using System.Net;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.EventStreams;

[ClassDataSource<ClusterFixture>(Shared = SharedType.Keyed, Key = McpCallerProtocol.FixtureKey)]
[NotInParallel]
internal sealed class AggregateReplayRf3Tests(ClusterFixture fixture)
{
    [Test]
    public async Task AcEvent007010SdkAndOfficialMcpMatchSnapshotTailReferenceAndRejectInvalidSlices()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await AggregateReplayRf3Scenario.CreateAsync(fixture, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, scenario.WorkerSecret);
        var append = await scenario.AppendInitialAsync(sdk, Guid.NewGuid(), deadline.Token);
        await Assert.That(append.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        var snapshot = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(
            scenario.InitialSnapshotCommand(Guid.NewGuid()), deadline.Token));
        await Assert.That(snapshot.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        await Assert.That(snapshot.Mutations.Single().Revision).IsEqualTo(AggregateReplayRf3Tokens.FirstSnapshotVersion);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            scenario.WorkerSecret, deadline.Token);
        var request = scenario.ReadRequest();
        var sdkPage = McpCallerAssertions.SdkSuccessAsync(await sdk.ReadAggregateReplayAsync(request, deadline.Token));
        var mcpPage = McpCallerAssertions.SuccessAsync<AggregateReplayPage>(await mcp.CallAsync(
            McpCallerTools.StreamsReplay, request, deadline.Token));
        await AggregateReplayRf3Assertions.SamePageAsync(await sdkPage, (await mcpPage).Value,
            expectedCount: AggregateReplayRf3Tokens.InitialReducedCount,
            expectedTailCount: AggregateReplayRf3Tokens.TailCount,
            expectedEventIds: [AggregateReplayRf3Tokens.PaidEventId, AggregateReplayRf3Tokens.ShippedEventId],
            expectedSnapshotState: AggregateReplayRf3Tokens.InitialState);

        await AggregateReplayRf3Assertions.ErrorAsync(sdk, mcp,
            request with { ReducerVersion = AggregateReplayRf3Tokens.UnknownReducer },
            ErrorCode.FormatUnsupported, deadline.Token);
        await AggregateReplayRf3Assertions.ErrorAsync(sdk, mcp,
            request with { StateSchemaVersion = AggregateReplayRf3Tokens.UnknownStateSchema },
            ErrorCode.FormatUnsupported, deadline.Token);
        await AggregateReplayRf3Assertions.ErrorAsync(sdk, mcp,
            request with { Stream = request.Stream with { Generation = AggregateReplayRf3Tokens.Generation + 1 } },
            ErrorCode.TokenInvalidated, deadline.Token);
        await AggregateReplayRf3Assertions.ErrorAsync(sdk, mcp,
            request with { MaximumEvents = AggregateReplayRf3Tokens.TooSmallTail },
            ErrorCode.BudgetExceeded, deadline.Token);
    }

    [Test]
    public async Task AcEvent009PersistedRawGrantsAuthorizeExactInputsAndRevocationDeniesBothClients()
    {
        using var deadline = McpCallerDeadline.Create();
        var scenario = await AggregateReplayRf3Scenario.CreateAsync(fixture, deadline.Token);
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
        var sdk = new KeyLoadClient(http, scenario.WorkerSecret);
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            scenario.WorkerSecret, deadline.Token);
        var request = await VerifyPersistedRawGrantsAsync(scenario, sdk, mcp, deadline.Token);
        await scenario.RevokeAsync(fixture, deadline.Token);
        await AssertRevokedPrincipalDeniedAsync(sdk, mcp, request, scenario.WorkerSecret, deadline.Token);
    }

    private async Task AssertRevokedPrincipalDeniedAsync(KeyLoadClient sdk, McpOfficialClient mcp,
        ReadAggregateReplayRequest request, string secret, CancellationToken cancellationToken)
    {
        var sdkResult = await sdk.ReadAggregateReplayAsync(request, cancellationToken);
        await Assert.That(sdkResult.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.Unauthenticated));
        var error = await Assert.ThrowsAsync<HttpRequestException>(() =>
            mcp.CallAsync(McpCallerTools.StreamsReplay, request, cancellationToken));
        await Assert.That(error!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(error.Message.Contains(secret, StringComparison.Ordinal)).IsFalse();
        await McpUnauthorizedProbe.VerifyAsync(fixture, McpCallerProtocol.Node3, secret, cancellationToken);
    }

    private async Task<ReadAggregateReplayRequest> VerifyPersistedRawGrantsAsync(
        AggregateReplayRf3Scenario scenario, KeyLoadClient sdk, McpOfficialClient mcp,
        CancellationToken cancellationToken)
    {
        var append = await scenario.AppendInitialAsync(sdk, Guid.NewGuid(), cancellationToken);
        await Assert.That(append.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        var snapshot = await McpCallerAssertions.SdkSuccessAsync(await sdk.CommitAsync(
            scenario.InitialSnapshotCommand(Guid.NewGuid()), cancellationToken));
        await Assert.That(snapshot.Durability).IsEqualTo(DurabilityProfile.QuorumProcessDurable);
        var request = scenario.ReadRequest();
        var sdkPage = McpCallerAssertions.SdkSuccessAsync(await sdk.ReadAggregateReplayAsync(request, cancellationToken));
        var mcpPage = McpCallerAssertions.SuccessAsync<AggregateReplayPage>(await mcp.CallAsync(
            McpCallerTools.StreamsReplay, request, cancellationToken));
        var sdkValue = await sdkPage;
        var mcpValue = (await mcpPage).Value;
        await AggregateReplayRf3Assertions.SamePageAsync(sdkValue, mcpValue,
            AggregateReplayRf3Tokens.InitialReducedCount, AggregateReplayRf3Tokens.TailCount,
            [AggregateReplayRf3Tokens.PaidEventId, AggregateReplayRf3Tokens.ShippedEventId],
            AggregateReplayRf3Tokens.InitialState);
        using var payload = JsonDocument.Parse(sdkValue.Events[0].Data.PayloadJson);
        using var headers = JsonDocument.Parse(sdkValue.Events[0].Data.HeadersJson);
        await Assert.That(payload.RootElement.GetProperty(AggregateReplayRf3Tokens.SecretProperty).GetString())
            .IsEqualTo(AggregateReplayRf3Tokens.Secret);
        await Assert.That(headers.RootElement.GetProperty(AggregateReplayRf3Tokens.PrivateHeaderProperty).GetString())
            .IsEqualTo(AggregateReplayRf3Tokens.PrivateHeader);
        var restricted = await scenario.CreateMissingGrantWorkerAsync(fixture, cancellationToken);
        using var restrictedHttp = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var restrictedSdk = new KeyLoadClient(restrictedHttp, restricted.Secret);
        await using var restrictedMcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3,
            restricted.Secret, cancellationToken);
        var denied = await AggregateReplayRf3Assertions.ErrorAsync(restrictedSdk, restrictedMcp,
            request, ErrorCode.PermissionDenied, cancellationToken);
        await McpCallerAssertions.DoesNotDiscloseAsync(denied, restricted.Secret, AggregateReplayRf3Tokens.Secret);
        await McpCallerAssertions.DoesNotDiscloseAsync(denied, restricted.Secret, AggregateReplayRf3Tokens.PrivateHeader);
        return request;
    }
}
