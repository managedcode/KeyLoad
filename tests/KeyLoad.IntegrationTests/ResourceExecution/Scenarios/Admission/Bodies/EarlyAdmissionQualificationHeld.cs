using System.Net.Http.Headers;
using System.Net.Http.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using static KeyLoad.IntegrationTests.Features.ResourceExecution.EarlyAdmissionQualificationProtocol;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class EarlyAdmissionQualificationHeld
{
    private const string ContentType = "application/json";
    private const string Bearer = "Bearer";
    private const string ControlRoute = "/v1/admin/dispatch?paused=false";
    internal static async Task RunAsync(ClusterFixture fixture, RequestCqrsClusterCallers root,
        RequestCqrsClusterCallers reader, EarlyAdmissionQualificationState state, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node1);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var bytes = JsonDefaults.Serialize(new QueryRequest(state.Partition, EarlyAdmissionQualificationProtocol.Query, AllowFullScan: true));
                var content = new EarlyAdmissionQualificationBody(bytes);
                using var request = new HttpRequestMessage(HttpMethod.Post, QueryRoute) { Content = content };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue(ContentType);
                request.Headers.Authorization = new AuthenticationHeaderValue(Bearer, fixture.AdminKey);
                Task<HttpResponseMessage>? pending = null;
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var baseline = await QuietAsync(root, token);
                    pending = http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                    var stage = await Task.WhenAny(content.Started, pending).WaitAsync(token);
                    await Assert.That(ReferenceEquals(stage, content.Started)).IsTrue();
                    await content.Started;
                    await HeldAsync(root, baseline, bytes.Length, token);
                    await EarlyAdmissionQualificationReads.WaveAsync(root, reader, state,
                        true, ErrorCode.ResourceExhausted, true, token);
                    await ControlAsync(http, fixture.AdminKey, token);
                }, failures);
                content.Release();
                if (pending is not null)
                {
                    await ServerFailureObserver.ObserveAsync(async () =>
                    {
                        using var response = await pending;
                        await ServerFailureObserver.ObserveAsync(async () =>
                        {
                            response.EnsureSuccessStatusCode();
                            var page = await response.Content.ReadFromJsonAsync<QueryPage>(JsonDefaults.Options, token);
                            await Assert.That(page).IsNotNull();
                            await EarlyAdmissionQualificationCurrent.PageAsync(page!);
                        }, failures);
                    }, failures);
                }
                using var cleanup = new CancellationTokenSource(RequestCqrsClusterProtocol.CleanupDeadline, TimeProvider.System);
                await ServerFailureObserver.ObserveAsync(async () => _ = await QuietAsync(root, cleanup.Token), failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task<long> QuietAsync(RequestCqrsClusterCallers root, CancellationToken token)
    {
        while (true)
        {
            var status = (await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.AdmissionStatusAsync(token))).Http!;
            if (status.Node.Commands == QuietCommands && status.VerifiedScopes.Commands == QuietCommands
                && status.Node.ControlCommands == EmptyControls && status.VerifiedScopes.ControlCommands == EmptyControls)
            {
                return status.Node.RetainedBytes;
            }
            await Task.Delay(Poll, token);
        }
    }
    private static async Task HeldAsync(RequestCqrsClusterCallers root, long baseline, int bodyBytes, CancellationToken token)
    {
        while (true)
        {
            var status = (await McpCallerAssertions.SdkSuccessAsync(await root.Sdk.AdmissionStatusAsync(token))).Http!;
            if (status.Node.Commands == HeldCommands && status.VerifiedScopes.Commands == HeldCommands
                && status.Node.RetainedBytes == baseline + HeavyBytes + EnvelopeBytes + bodyBytes * BodyScale)
            {
                return;
            }
            await Task.Delay(Poll, token);
        }
    }
    private static async Task ControlAsync(HttpClient http, string secret, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, ControlRoute);
            request.Headers.Authorization = new AuthenticationHeaderValue(Bearer, secret);
            request.Headers.Add(ServerProtocol.CommandHeader, Guid.NewGuid().ToString());
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var response = await http.SendAsync(request, token);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    response.EnsureSuccessStatusCode();
                    await Assert.That(await response.Content.ReadFromJsonAsync<bool>(JsonDefaults.Options, token)).IsTrue();
                }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
