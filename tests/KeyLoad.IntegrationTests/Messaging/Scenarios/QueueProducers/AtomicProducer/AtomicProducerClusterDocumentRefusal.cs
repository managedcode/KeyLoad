using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class AtomicProducerClusterDocumentRefusal
{
    internal static CommandRequest Create(QueueProducerClusterSeed seed)
        => new(Guid.NewGuid(), seed.Lane.Partition,
            [QueueProducerClusterSetup.Event(QueueProducerClusterProtocol.Refused, QueueProducerClusterProtocol.HealthyPayload),
             new PutDocument(QueueProducerClusterProtocol.Collection, QueueProducerClusterProtocol.Original,
                 QueueProducerClusterProtocol.HealthyPayload, ExpectedRevision: QueueProducerClusterProtocol.AbsentDocumentRevision),
             new EnqueueMessage(seed.Lane.Queue, QueueProducerClusterProtocol.Refused,
                 QueueProducerClusterProtocol.HealthyPayload, QueueProducerClusterProtocol.Headers)]);

    internal static async Task<JsonElement> ExecuteAsync(ClusterFixture fixture, QueueProducerClusterOriginal original,
        CommandRequest command, JsonElement? expected, List<Exception> failures, CancellationToken token)
    {
        var seed = original.Seed;
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var publisher = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        JsonElement problem = default;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            problem = await AtomicProducerClusterDocumentRefusalAssertions.RequireAsync(publisher, mcp,
                original, command, expected, token);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return problem;
    }
}
