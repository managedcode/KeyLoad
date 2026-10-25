using KeyLoad.Client;
using KeyLoad.ClusterTests.ClientApi;
using KeyLoad.ClusterTests.QueryExecution;
using ModelContextProtocol.Client;

namespace KeyLoad.ClusterTests.Authorization;

internal static class AuthenticationExecutionValues
{
    internal static async Task HealthyAsync(KeyLoadClient reader, McpClient mcp,
        AuthenticationExecutionHttpHandler http, AuthenticationExecutionCollector collector,
        AuthenticationExecutionState state, DocumentResult expected, CancellationToken token)
    {
        var position = collector.Position;
        var sent = http.Requests;
        await SqlClusterProtocol.EqualAsync<DocumentResult?>(expected,
            await PublicReadAssertions.DocumentAsync(await reader.GetAsync(state.Document.Reference, token)));
        await AuthenticationExecutionAssertions.WindowAsync(collector, position, http.Requests - sent, state.Reader.Id);
        position = collector.Position;
        sent = http.Requests;
        var actual = await PublicReadAssertions.RequiredDocumentAsync(await AuthenticationExecutionClients.ReadAsync(mcp, state, token));
        await SqlClusterProtocol.EqualAsync(expected, actual.Value);
        await AuthenticationExecutionAssertions.WindowAsync(collector, position, http.Requests - sent, state.Reader.Id);
    }

    internal static async Task<string> ImageAsync(KeyLoadClient root, AuthenticationExecutionState state, CancellationToken token)
    {
        var document = await PublicReadAssertions.DocumentAsync(await root.GetAsync(state.Document.Reference, token));
        await SqlClusterProtocol.EqualAsync<DocumentResult?>(state.Expected, document);
        var outbox = await McpCallerAssertions.SdkSuccessAsync(await root.OutboxStatusAsync(state.Document.Partition, token));
        return System.Text.Json.JsonSerializer.Serialize(new
        {
            Document = document,
            Outbox = outbox
        }, JsonDefaults.Options);
    }

    internal static async Task ReplayAsync(KeyLoadClient root, AuthenticationExecutionState state, CancellationToken token)
    {
        var before = await ImageAsync(root, state, token);
        await SqlClusterProtocol.EqualAsync(state.OriginalReceipt,
            await McpCallerAssertions.SdkSuccessAsync(await root.CommitAsync(state.OriginalCommand, token)));
        await Assert.That(await ImageAsync(root, state, token)).IsEqualTo(before);
    }
}
