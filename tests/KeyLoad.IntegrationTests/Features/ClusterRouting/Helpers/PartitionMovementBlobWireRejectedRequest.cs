using System.Net;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using KeyLoad.Server.Features.BlobStorage;
using KeyLoad.Server.Features.DocumentStorage;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementBlobWireRejectedRequest
{
    private const int FirstVoter = 0;

    internal static async Task RequireAsync(HttpClient client, RemoteControlledBlobCall original,
        byte[] changed, string signature, CancellationToken token)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                new Uri(new Uri(original.Destination.Endpoints[FirstVoter]), RemoteDocumentProtocol.Path));
            request.Content = new ByteArrayContent(changed);
            request.Content.Headers.ContentType = new(RemoteDocumentProtocol.ContentType);
            request.Headers.Add(RemoteDocumentProtocol.SignatureHeader, signature);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.Unauthorized);
                    await response.Content.LoadIntoBufferAsync(original.MaximumReplyBytes, token);
                    var bytes = await response.Content.ReadAsByteArrayAsync(token);
                    using var problem = System.Text.Json.JsonDocument.Parse(bytes);
                    await McpCallerAssertions.VerifyProblemAsync(problem.RootElement, ErrorCode.Unauthenticated);
                }, failures);
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
