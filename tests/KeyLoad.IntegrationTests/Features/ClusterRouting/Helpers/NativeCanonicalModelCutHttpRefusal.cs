using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;
using KeyLoad.Server.Features.ClusterRouting;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class NativeCanonicalModelCutHttpRefusal
{
    private const string BearerScheme = "Bearer";
    private const int OverflowByte = 1;
    private const string Invalid = "The genuine native diagnostic refusal exceeded its bounded public error contract.";

    internal static async Task RequireValidationAsync(TwoRf3MembershipWave wave, string node,
        string adminCredential, NativeCanonicalModelCutRequest original, CancellationToken token)
    {
        var failures = new List<Exception>();
        HttpClient? client = null;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            client = McpCallerHttp.Create(wave.Application, node);
            using var request = new HttpRequestMessage(HttpMethod.Post, NativeCanonicalModelCutDiagnosticProtocol.Path);
            request.Headers.Authorization = new AuthenticationHeaderValue(BearerScheme, adminCredential);
            request.Content = new ByteArrayContent(NativeSerialization.Serialize(original));
            request.Content.Headers.ContentType = new MediaTypeHeaderValue(NativeCanonicalModelCutDiagnosticProtocol.ContentType);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            await Assert.That((int)response.StatusCode).IsEqualTo(Errors.Status(ErrorCode.Validation));
            await Assert.That(response.Headers.Contains(NativeCanonicalModelCutDiagnosticProtocol.SignatureHeader)).IsFalse();
            var bytes = await ReadBoundedAsync(response, token).ConfigureAwait(false);
            await Assert.That(Encoding.UTF8.GetString(bytes).Contains(adminCredential, StringComparison.Ordinal)).IsFalse();
            using var problem = JsonDocument.Parse(bytes);
            await McpCallerAssertions.VerifyProblemAsync(problem.RootElement, ErrorCode.Validation).ConfigureAwait(false);
            await Assert.That(problem.RootElement.GetProperty(McpCallerProtocol.ProblemDetail).GetString())
                .IsEqualTo(NativeCanonicalModelCutCodec.Invalid().Message);
            token.ThrowIfCancellationRequested();
        }, failures).ConfigureAwait(false);
        if (client is { } owned)
        { ServerFailureObserver.Observe(owned.Dispose, failures); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task<byte[]> ReadBoundedAsync(HttpResponseMessage response, CancellationToken token)
    {
        var maximum = NativeCanonicalModelCutCodec.MaximumRecordBytes;
        if (response.Content.Headers.ContentLength is { } length && (length <= 0 || length > maximum))
        { throw new InvalidDataException(Invalid); }
        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        var bytes = new byte[maximum + OverflowByte];
        var total = 0;
        while (total < bytes.Length)
        {
            var count = await stream.ReadAsync(bytes.AsMemory(total), token).ConfigureAwait(false);
            if (count == 0)
            { break; }
            total += count;
        }
        if (total <= 0 || total > maximum
            || response.Content.Headers.ContentLength is { } expected && expected != total)
        { throw new InvalidDataException(Invalid); }
        token.ThrowIfCancellationRequested();
        return bytes.AsSpan(0, total).ToArray();
    }
}
