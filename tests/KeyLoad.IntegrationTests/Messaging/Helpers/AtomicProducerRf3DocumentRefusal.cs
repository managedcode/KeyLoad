using System.Text.Json;
using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;
using KeyLoad.Server;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class AtomicProducerRf3DocumentRefusal
{
    internal static CommandRequest Create(QueueProducerRf3Seed seed)
        => new(Guid.NewGuid(), seed.Lane.Partition,
            [QueueProducerRf3Setup.Event(QueueProducerRf3Protocol.Refused, QueueProducerRf3Protocol.HealthyPayload),
             new PutDocument(QueueProducerRf3Protocol.Collection, QueueProducerRf3Protocol.Original,
                 QueueProducerRf3Protocol.HealthyPayload, ExpectedRevision: QueueProducerRf3Protocol.AbsentDocumentRevision),
             new EnqueueMessage(seed.Lane.Queue, QueueProducerRf3Protocol.Refused,
                 QueueProducerRf3Protocol.HealthyPayload, QueueProducerRf3Protocol.Headers)]);

    internal static async Task<JsonElement> ExecuteAsync(ClusterFixture fixture, QueueProducerRf3Original original,
        CommandRequest command, JsonElement? expected, List<Exception> failures, CancellationToken token)
    {
        var seed = original.Seed;
        using var http = McpCallerHttp.Create(fixture, McpCallerProtocol.Node2);
        var publisher = new KeyLoadClient(http, seed.Identity.Secret, IntegrationClientOptions.Execution());
        await using var mcp = await McpOfficialClient.ConnectAsync(fixture, McpCallerProtocol.Node3, seed.Identity.Secret, token);
        JsonElement problem = default;
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            problem = await AtomicProducerRf3DocumentRefusalAssertions.RequireAsync(publisher, mcp,
                original, command, expected, token);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
        return problem;
    }
}
