using System.Net;
using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.IntegrationTests.Features.ClusterRouting.Helpers;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class RequestCqrsRf3McpGuardEvidenceCallAssertions
{
    private const string OversizedProblemMessage = "The fixed MCP guard validation response exceeded its byte bound.";

    internal static async Task VerifyValidationAsync(HttpResponseMessage response, RequestCqrsLifecycleEvidence lifecycle,
        CancellationToken cancellationToken)
    {
        await Assert.That(response.StatusCode).IsEqualTo(HttpStatusCode.BadRequest);
        var bytes = await ReadProblemAsync(response, lifecycle, cancellationToken).ConfigureAwait(false);
        lifecycle.SetStage(RequestCqrsLifecycleStage.GuardProblemValidation);
        using var body = JsonDocument.Parse(bytes);
        await McpCallerAssertions.VerifyProblemAsync(body.RootElement, ErrorCode.Validation).ConfigureAwait(false);
    }

    private static async Task<byte[]> ReadProblemAsync(HttpResponseMessage response, RequestCqrsLifecycleEvidence lifecycle,
        CancellationToken cancellationToken)
    {
        lifecycle.SetStage(RequestCqrsLifecycleStage.GuardBodyRead);
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        var buffer = new byte[McpCallerProtocol.ErrorByteLimit + 1];
        var total = 0;
        while (total < buffer.Length)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(total), cancellationToken).ConfigureAwait(false);
            if (count == 0)
            { break; }
            total += count;
        }
        if (total > McpCallerProtocol.ErrorByteLimit)
        { throw new InvalidDataException(OversizedProblemMessage); }
        lifecycle.SetStage(RequestCqrsLifecycleStage.GuardBodyReaderDispose);
        return buffer[..total];
    }
}
