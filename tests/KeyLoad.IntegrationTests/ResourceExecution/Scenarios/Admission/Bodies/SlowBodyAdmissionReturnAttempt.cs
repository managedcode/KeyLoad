using System.Net.Http.Headers;
using Aspire.Hosting.Testing;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting;
using KeyLoad.Server;
using static KeyLoad.IntegrationTests.Features.ResourceExecution.EarlyAdmissionQualificationProtocol;

namespace KeyLoad.IntegrationTests.Features.ResourceExecution;

internal static class SlowBodyAdmissionReturnAttempt
{
    private const string UnexpectedResponse = "The incomplete original request unexpectedly returned an HTTP response: ";

    internal static async Task RunAsync(ClusterFixture fixture, RequestCqrsClusterCallers root,
        EarlyAdmissionQualificationState state, SlowBodyAdmissionReturnMode mode, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var transport = new SlowBodyAdmissionReturnTransport(fixture.App.GetEndpoint(McpCallerProtocol.Node1, McpCallerProtocol.HttpEndpoint));
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var canceled = CancellationTokenSource.CreateLinkedTokenSource(token);
                var bytes = JsonDefaults.Serialize(new QueryRequest(state.Partition, EarlyAdmissionQualificationProtocol.Query, AllowFullScan: true));
                var body = new SlowBodyAdmissionReturnBody(bytes);
                using var request = new HttpRequestMessage(HttpMethod.Post, QueryRoute) { Content = body };
                request.Content.Headers.ContentType = new MediaTypeHeaderValue(SlowBodyAdmissionReturnProtocol.ContentType);
                request.Headers.Authorization = new AuthenticationHeaderValue(SlowBodyAdmissionReturnProtocol.Bearer, fixture.AdminKey);
                Task<HttpResponseMessage>? pending = null;
                HttpAdmissionStatus? baseline = null;
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    baseline = await SlowBodyAdmissionReturnCounters.BaselineAsync(root, token);
                    pending = transport.Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, canceled.Token);
                    await Assert.That(ReferenceEquals(await Task.WhenAny(body.Started, pending).WaitAsync(token), body.Started)).IsTrue();
                    await body.Started;
                    await SlowBodyAdmissionReturnCounters.HeldAsync(root, baseline, bytes.Length, token);
                    await Assert.That(pending.IsCompleted).IsFalse();
                    if (mode == SlowBodyAdmissionReturnMode.CallerCancellation)
                    {
                        await canceled.CancelAsync();
                    }
                    else
                    {
                        transport.Disconnect();
                    }
                    await SlowBodyAdmissionReturnCounters.ExactAsync(root, baseline, token);
                    await Assert.That(body.Released).IsFalse();
                    await EarlyAdmissionQualificationCurrent.FullAsync(root, state, token);
                }, failures);
                await SettleAsync(transport, body, canceled, pending, baseline, root, mode, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
    private static async Task SettleAsync(SlowBodyAdmissionReturnTransport transport,
        SlowBodyAdmissionReturnBody body, CancellationTokenSource canceled, Task<HttpResponseMessage>? pending,
        HttpAdmissionStatus? baseline, RequestCqrsClusterCallers root, SlowBodyAdmissionReturnMode mode,
        List<Exception> failures)
    {
        // Abort independently even when the hold or counter oracle failed; preserve every original failure.
        ServerFailureObserver.Observe(() => transport.Disconnect(requireConnected: false), failures);
        body.Release();
        using var cleanup = new CancellationTokenSource(RequestCqrsClusterProtocol.CleanupDeadline, TimeProvider.System);
        if (pending is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => TerminalAsync(pending, mode, canceled.Token, cleanup.Token), failures);
            // Force cancellation only after preserving the first native terminal observation.
            await ServerFailureObserver.ObserveAsync(canceled.CancelAsync, failures);
            if (!pending.IsCompleted)
            {
                using var finalJoin = new CancellationTokenSource(RequestCqrsClusterProtocol.CleanupDeadline, TimeProvider.System);
                await ServerFailureObserver.ObserveAsync(() => TerminalAsync(pending, mode, canceled.Token, finalJoin.Token), failures);
                if (body.Entered.IsCompleted)
                {
                    await ServerFailureObserver.ObserveAsync(() => body.Finished.WaitAsync(finalJoin.Token), failures);
                }
            }
            else if (body.Entered.IsCompleted)
            {
                await ServerFailureObserver.ObserveAsync(() => body.Finished.WaitAsync(cleanup.Token), failures);
            }
        }
        if (baseline is not null)
        {
            await ServerFailureObserver.ObserveAsync(() => SlowBodyAdmissionReturnCounters.ExactAsync(root, baseline, cleanup.Token), failures);
        }
    }

    private static async Task TerminalAsync(Task<HttpResponseMessage> pending, SlowBodyAdmissionReturnMode mode,
        CancellationToken original, CancellationToken cleanup)
    {
        try
        {
            using var response = await pending.WaitAsync(cleanup);
            throw new InvalidOperationException(UnexpectedResponse + response.StatusCode);
        }
        catch (OperationCanceledException) when (pending.IsCanceled && original.IsCancellationRequested
            && mode == SlowBodyAdmissionReturnMode.CallerCancellation)
        {
        }
        catch (HttpRequestException) when (mode == SlowBodyAdmissionReturnMode.PhysicalDisconnect)
        {
        }
        catch (IOException) when (mode == SlowBodyAdmissionReturnMode.PhysicalDisconnect)
        {
        }
    }
}
