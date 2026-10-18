using System.Net;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.IntegrationTests.Features.QueryExecution;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal static class GuideLifecyclePublicControls
{
    private const string UnexpectedArgument = "unexpected";

    private static Func<CancellationToken, Task>[] Valid(McpOfficialClient session) =>
    [
        token => session.Client.ListResourcesAsync(new ListResourcesRequestParams(), token).AsTask(),
        token => session.Client.ReadResourceAsync(GuideLifecyclePublicGuidance.Resource, cancellationToken: token).AsTask(),
        token => session.Client.ListPromptsAsync(new ListPromptsRequestParams(), token).AsTask(),
        token => session.Client.GetPromptAsync(GuideLifecyclePublicExpected.PromptName, cancellationToken: token).AsTask()
    ];

    private static Func<CancellationToken, Task>[] Invalid(McpOfficialClient session) =>
    [
        token => session.Client.ListResourcesAsync(new ListResourcesRequestParams { Cursor = "next" }, token).AsTask(),
        token => session.Client.ReadResourceAsync(new Uri("keyload://guides/missing", UriKind.Absolute), cancellationToken: token).AsTask(),
        token => session.Client.ListPromptsAsync(new ListPromptsRequestParams { Cursor = "next" }, token).AsTask(),
        token => session.Client.GetPromptAsync("keyload_agent_missing", cancellationToken: token).AsTask(),
        token => session.Client.GetPromptAsync(GuideLifecyclePublicExpected.PromptName,
            new Dictionary<string, object?> { [UnexpectedArgument] = true }, cancellationToken: token).AsTask()
    ];

    internal static async Task RefusalsAndCancellationAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, GuideLifecyclePublicState state, CancellationToken token)
    {
        foreach (var call in Invalid(reader.Mcp))
        {
            var error = await Assert.ThrowsAsync<McpProtocolException>(() => call(token));
            await Assert.That(error).IsNotNull();
            await Assert.That(error!.ErrorCode).IsEqualTo(McpErrorCode.InvalidParams);
            await FollowingAsync(administrator, reader, state, token);
        }
        foreach (var call in Valid(reader.Mcp))
        {
            using var canceled = CancellationTokenSource.CreateLinkedTokenSource(token);
            await canceled.CancelAsync();
            await Assert.ThrowsAsync<OperationCanceledException>(() => call(canceled.Token));
            await FollowingAsync(administrator, reader, state, token);
        }
    }

    private static async Task FollowingAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, GuideLifecyclePublicState state, CancellationToken token)
    {
        await GuideLifecyclePublicValues.CurrentAsync(administrator, state, token);
        await GuideLifecyclePublicGuidance.HealthyAsync(reader.Mcp, token);
        await GuideLifecyclePublicValues.DocumentAsync(reader, state, token);
        await GuideLifecyclePublicValues.SeedReplayAsync(administrator, state, token);
    }

    internal static async Task RevokedAsync(RequestCqrsClusterCallers administrator,
        RequestCqrsClusterCallers reader, GuideLifecyclePublicState state, CancellationToken token)
    {
        foreach (var call in Valid(reader.Mcp))
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() => call(token));
            await UnauthorizedAsync(error, state);
            await GuideLifecyclePublicValues.CurrentAsync(administrator, state, token);
            await GuideLifecyclePublicValues.SeedReplayAsync(administrator, state, token);
        }
        await SdkRevokedAsync(reader.Sdk, state, token);
        await GuideLifecyclePublicValues.CurrentAsync(administrator, state, token);
    }

    internal static async Task UnauthorizedAsync(HttpRequestException? error, GuideLifecyclePublicState state)
    {
        await Assert.That(error).IsNotNull();
        await Assert.That(error!.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
        await Assert.That(error.Message.Contains(state.Identity.Secret, StringComparison.Ordinal)).IsFalse();
        await Assert.That(error.Message.Contains(GuideLifecyclePublicPreparation.InitialJson, StringComparison.Ordinal)).IsFalse();
    }

    internal static async Task SdkRevokedAsync(global::KeyLoad.Client.KeyLoadClient reader,
        GuideLifecyclePublicState state, CancellationToken token)
    {
        var actual = await reader.GetAsync(state.Scenario.Reference, state.SeedReceipt.Token, token);
        await Assert.That(actual.IsFailed).IsTrue();
        await Assert.That(actual.Value).IsNull();
        await SqlClusterProtocol.EqualAsync(Errors.Problem(ErrorCode.Unauthenticated,
            "The credential is unavailable or expired."), actual.Problem);
    }
}
