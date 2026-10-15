using System.Net.Http.Headers;
using System.Net.Http.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class HeavyRouteRf3Held
{
    private const string Path = "/v1/graph/shortest-path";
    internal static async Task RunAsync(ClusterFixture fixture, RequestCqrsRf3Callers root,
        RequestCqrsRf3Callers reader, HeavyRouteRf3State state, CancellationToken token)
    {
        var failures = new List<Exception>();
        HttpClient? http = null;
        HeavyRouteHeldBody? content = null;
        HttpRequestMessage? request = null;
        Task<HttpResponseMessage>? pending = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
            var body = JsonDefaults.Serialize(state.Path);
            content = new HeavyRouteHeldBody(body);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            request = new HttpRequestMessage(HttpMethod.Post, Path) { Content = content };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", fixture.AdminKey);
            var baseline = await AwaitQuietAsync(root, token);
            pending = http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
            await AwaitHeldAsync(root, baseline, body.Length, token);
            await HeavyRouteRf3Operations.RunAsync(root, reader, state, ErrorCode.ResourceExhausted, token);
            await ControlAsync(http, fixture.AdminKey, token);
        }, failures);
        content?.Release();
        if (pending is not null)
        {
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var response = await pending;
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    response.EnsureSuccessStatusCode();
                    var result = await response.Content.ReadFromJsonAsync<GraphShortestPathResult>(JsonDefaults.Options, token);
                    await Assert.That(result).IsNotNull();
                    await HeavyRouteRf3Results.PathAsync(result!, state);
                }, failures);
            }, failures);
        }
        await ServerFailureObserver.ObserveAsync(async () => _ = await AwaitQuietAsync(root, token), failures);
        ServerFailureObserver.Observe(() => request?.Dispose(), failures);
        ServerFailureObserver.Observe(() => content?.Dispose(), failures);
        ServerFailureObserver.Observe(() => http?.Dispose(), failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private const long HeavyWorkingBytes = 65_536L;
    private const long EnvelopeBytes = 4_096L;
    private const long BodyReservationScale = 4L;
    private static async Task AwaitHeldAsync(RequestCqrsRf3Callers root, long baseline, int bodyBytes, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var status = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.AdmissionStatusAsync(token));
            if (status.Http!.Node.Commands == 2 && status.Http.VerifiedScopes.Commands == 2
                && status.Http.Node.RetainedBytes == baseline + HeavyWorkingBytes + EnvelopeBytes + bodyBytes * BodyReservationScale)
            {
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(10), token);
        }
    }
    private static async Task<long> AwaitQuietAsync(RequestCqrsRf3Callers root, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            var status = await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.AdmissionStatusAsync(token));
            if (status.Http!.Node.Commands == 1 && status.Http.VerifiedScopes.Commands == 1
                && status.Http.Node.ControlCommands == 0 && status.Http.VerifiedScopes.ControlCommands == 0)
            {
                return status.Http.Node.RetainedBytes;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(10), token);
        }
    }

    private static async Task ControlAsync(HttpClient http, string key, CancellationToken token)
    {
        using var control = new HttpRequestMessage(HttpMethod.Post, "/v1/admin/dispatch?paused=false");
        control.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        control.Headers.Add("X-KeyLoad-Command-Id", Guid.NewGuid().ToString());
        using var response = await http.SendAsync(control, token);
        response.EnsureSuccessStatusCode();
        await Assert.That(await response.Content.ReadFromJsonAsync<bool>(JsonDefaults.Options, token)).IsTrue();
    }
}
