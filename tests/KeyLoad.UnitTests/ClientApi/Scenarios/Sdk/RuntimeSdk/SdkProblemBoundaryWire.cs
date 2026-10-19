using System.Net.Http.Headers;
using System.Net.Http.Json;
using KeyLoad.Server;
using P = KeyLoad.UnitTests.Features.ClientApi.SdkProblemBoundaryProtocol;

namespace KeyLoad.UnitTests.Features.ClientApi;

internal static class SdkProblemBoundaryWire
{
    internal static async Task RequireAsync(KeyLoadClientKestrelServer server, SdkProblemBoundaryScenario state,
        SdkProblemBoundaryTasks tasks, bool declared, CancellationToken token)
    {
        var failures = new List<Exception>();
        HttpRequestMessage? message = null;
        HttpResponseMessage? response = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            message = new(HttpMethod.Post, P.Documents)
            {
                Content = JsonContent.Create(new GetDocumentRequest(state.Forbidden), options: JsonDefaults.Options)
            };
            message.Headers.Authorization = new AuthenticationHeaderValue(P.Scheme, P.Secret);
            response = await tasks.CompleteAsync(tasks.Retain(server.Client.SendAsync(message,
                HttpCompletionOption.ResponseHeadersRead, token)));
            await Assert.That((int)response.StatusCode).IsEqualTo(Errors.Status(ErrorCode.PermissionDenied));
            await Assert.That(response.Content.Headers.ContentLength).IsEqualTo(declared ? (long?)SdkProblemBoundaryScenario.ProblemBytes : null);
            await Assert.That(response.Headers.TransferEncodingChunked == true).IsEqualTo(!declared);
            var bytes = new byte[SdkProblemBoundaryScenario.ProblemBytes + P.Lookahead];
            var length = P.Empty;
            var input = await response.Content.ReadAsStreamAsync(token);
            while (length < bytes.Length)
            {
                var read = await input.ReadAsync(bytes.AsMemory(length), token);
                if (read == P.Empty)
                {
                    break;
                }
                length += read;
            }
            await Assert.That(length).IsEqualTo(SdkProblemBoundaryScenario.ProblemBytes);
            await Assert.That(bytes.AsSpan(P.Empty, length).SequenceEqual(JsonDefaults.Serialize(SdkProblemBoundaryScenario.ExpectedProblem))).IsTrue();
        }, failures);
        if (response is not null)
        {
            ServerFailureObserver.Observe(response.Dispose, failures);
        }
        if (message is not null)
        {
            ServerFailureObserver.Observe(message.Dispose, failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
