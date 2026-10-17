using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.Search;

internal static class AdaptiveFilteredPublicRf3Refusals
{
    internal static async Task DeniedAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader,
        AdaptiveFilteredPublicRf3State state, ApproximateSearchRequest request, string sdkDetail, string officialDetail,
        CancellationToken token)
    {
        await AdaptiveFilteredPublicRf3Current.ObserveAsync(administrator, state, async () =>
        {
            var actual = await reader.Sdk.ApproximateSearchAsync(request, token);
            await Assert.That(actual.IsSuccess).IsFalse();
            await Assert.That(actual.Value).IsNull();
            await AdaptiveFilteredPublicRf3Equal.RequireAsync(Errors.Problem(ErrorCode.PermissionDenied, sdkDetail), actual.Problem);
        }, token);
        await AdaptiveFilteredPublicRf3Current.ObserveAsync(administrator, state, async () =>
        {
            var actual = await reader.Mcp.CallAsync(AdaptiveFilteredPublicRf3Protocol.Tool, request, token);
            await McpCallerAssertions.ErrorAsync(actual, ErrorCode.PermissionDenied, dispatched: true);
            var expected = JsonDefaults.Deserialize<System.Text.Json.JsonElement>(
                JsonDefaults.Serialize(Errors.Problem(ErrorCode.PermissionDenied, officialDetail)));
            await AdaptiveFilteredPublicRf3Equal.RequireAsync(expected, actual.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error));
        }, token);
    }
    internal static async Task CancelAsync(RequestCqrsRf3Callers administrator, RequestCqrsRf3Callers reader,
        AdaptiveFilteredPublicRf3State state, AnnMaintenanceRequest pin, CancellationToken token)
    {
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(token);
        await cancelled.CancelAsync();
        var request = new ApproximateSearchRequest(AdaptiveFilteredPublicRf3Protocol.Version,
            AdaptiveFilteredPublicRf3Recipe.Search(state, null, AdaptiveFilteredPublicRf3Protocol.Limit), pin.Consumer, pin.IndexGeneration);
        await AdaptiveFilteredPublicRf3Current.ObserveAsync(administrator, state, async () =>
        {
            var original = reader.Sdk.ApproximateSearchAsync(request, cancelled.Token);
            var actual = await original;
            await Assert.That(actual.IsSuccess).IsFalse();
            await Assert.That(actual.Value).IsNull();
            await AdaptiveFilteredPublicRf3Equal.RequireAsync(Errors.Problem(ErrorCode.Cancelled,
                "The read response is unavailable."), actual.Problem);
        }, token);
        await AdaptiveFilteredPublicRf3Reads.RunAsync(administrator, reader, state, pin, token);
    }
}
