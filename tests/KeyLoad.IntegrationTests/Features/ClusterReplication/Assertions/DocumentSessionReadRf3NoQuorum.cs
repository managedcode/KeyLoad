using System.Net;
using System.Text;
using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

internal static class DocumentSessionReadRf3NoQuorum
{
    private const string NoLeader = "The cluster has no current leader with a reachable majority.";
    private const string NativeBodyMarker = "Response body: ";
    private const string ProblemType = "urn:keyload:error:OwnershipLost";
    private const int NativeFailureBytes = 1024;
    private const int Fields = 5;
    private const int ServiceUnavailable = 503;
    private const int FirstCharacter = 0;

    internal static async Task VerifyAsync(KeyLoadClient sdk, McpOfficialClient mcp, EntityRef reference,
        CommitToken minimum, string credential, CancellationToken token)
    {
        var denied = await sdk.GetAsync(reference, minimum, token);
        await Assert.That(denied.Problem?.ErrorCode).IsEqualTo(nameof(ErrorCode.OwnershipLost));
        await Assert.That(denied.Problem?.Detail).IsEqualTo(NoLeader);
        await Assert.That(denied.Problem?.StatusCode).IsEqualTo(ServiceUnavailable);
        var failure = await Assert.ThrowsExactlyAsync<HttpRequestException>(() => mcp.CallAsync(
            McpCallerTools.DocumentsGet, new GetDocumentRequest(reference, minimum), token))
            ?? throw new InvalidOperationException("The native official caller did not retain its no-quorum HTTP failure.");
        await Assert.That(failure.StatusCode).IsEqualTo(HttpStatusCode.ServiceUnavailable);
        await Assert.That(Encoding.UTF8.GetByteCount(failure.Message) <= NativeFailureBytes).IsTrue();
        await Assert.That(failure.Message.Contains(credential, StringComparison.Ordinal)).IsFalse();
        await Assert.That(failure.Message.Contains(DocumentSessionReadRf3Protocol.FirstJson, StringComparison.Ordinal)).IsFalse();
        var start = failure.Message.IndexOf(NativeBodyMarker, StringComparison.Ordinal);
        await Assert.That(start >= FirstCharacter).IsTrue();
        using var document = JsonDocument.Parse(failure.Message[(start + NativeBodyMarker.Length)..]);
        await ProblemAsync(document.RootElement);
    }

    private static async Task ProblemAsync(JsonElement body)
    {
        await Assert.That(body.ValueKind).IsEqualTo(JsonValueKind.Object);
        var fields = body.EnumerateObject().Select(property => property.Name).ToArray();
        await Assert.That(fields.Length).IsEqualTo(Fields);
        await Assert.That(new HashSet<string>(fields, StringComparer.Ordinal).SetEquals(McpCallerAssertions.ProblemFields)).IsTrue();
        await Assert.That(body.GetProperty(McpCallerProtocol.ProblemType).GetString()).IsEqualTo(ProblemType);
        await Assert.That(body.GetProperty(McpCallerProtocol.ProblemTitle).GetString()).IsEqualTo(nameof(ErrorCode.OwnershipLost));
        await Assert.That(body.GetProperty(McpCallerProtocol.ProblemStatus).GetInt32()).IsEqualTo(ServiceUnavailable);
        await Assert.That(body.GetProperty(McpCallerProtocol.ProblemDetail).GetString()).IsEqualTo(NoLeader);
        await Assert.That(body.GetProperty(McpCallerProtocol.ProblemCode).GetString()).IsEqualTo(nameof(ErrorCode.OwnershipLost));
    }
}
