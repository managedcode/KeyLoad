using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;

namespace KeyLoad.IntegrationTests.Messaging;

internal static class AtomicProducerClusterBoundary
{
    internal static async Task RefuseAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueProducerClusterSeed seed, CancellationToken token)
    {
        var eventConflict = new CommandRequest(Guid.NewGuid(), seed.Lane.Partition,
            [new PutDocument(QueueProducerClusterProtocol.Collection, QueueProducerClusterProtocol.Refused, QueueProducerClusterProtocol.HealthyPayload),
             QueueProducerClusterSetup.Event(QueueProducerClusterProtocol.Original, QueueProducerClusterProtocol.HealthyPayload),
             new EnqueueMessage(seed.Lane.Queue, QueueProducerClusterProtocol.Refused, QueueProducerClusterProtocol.HealthyPayload)]);
        await RefusedAsync(sdk, mcp, eventConflict, ErrorCode.RevisionConflict, token);
        await QueueProducerClusterAssertions.AbsentAsync(sdk, seed, QueueProducerClusterProtocol.Refused, QueueProducerClusterProtocol.Refused, token);
        var foreign = eventConflict with
        {
            CommandId = Guid.NewGuid(),
            Mutations =
            [new PutDocument(QueueProducerClusterProtocol.Collection, QueueProducerClusterProtocol.Refused, QueueProducerClusterProtocol.HealthyPayload),
             new AppendEvents(QueueProducerClusterProtocol.ForeignStreamSet, QueueProducerClusterProtocol.Refused,
                 [new(QueueProducerClusterProtocol.Refused, QueueProducerClusterProtocol.EventType, QueueProducerClusterProtocol.HealthyPayload)], ExpectedStreamRevision.NoStream),
             new EnqueueMessage(seed.Lane.Queue, QueueProducerClusterProtocol.Refused, QueueProducerClusterProtocol.HealthyPayload)]
        };
        await RefusedAsync(sdk, mcp, foreign, ErrorCode.Conflict, token);
        var partition = seed.Lane.Partition with
        {
            TransactionDomainId = QueueProducerClusterProtocol.ForeignDomain
        };
        var page = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadStreamAsync(new(new(partition,
            QueueProducerClusterProtocol.ForeignStreamSet, QueueProducerClusterProtocol.Refused)), token));
        await Assert.That(page.Stream).IsEqualTo(new StreamRef(partition, QueueProducerClusterProtocol.ForeignStreamSet, QueueProducerClusterProtocol.Refused));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Events).IsEmpty();
        await QueueProducerClusterAssertions.EqualAsync(page.Head, new StreamHead(QueueProducerClusterProtocol.EmptyStreamRevision,
            QueueProducerClusterProtocol.FirstAvailableEventRevision, QueueProducerClusterProtocol.InitialEventGeneration));
        await QueueProducerClusterAssertions.AbsentAsync(sdk, seed, QueueProducerClusterProtocol.Refused, QueueProducerClusterProtocol.Refused, token);
    }

    private static async Task RefusedAsync(KeyLoadClient sdk, McpOfficialClient mcp, CommandRequest command,
        ErrorCode expected, CancellationToken token)
    {
        await QueueProducerClusterAssertions.DeniedAsync(await sdk.CommitAsync(command, token), expected);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token), expected, true);
        await AtomicProducerClusterRoutes.RefusedAsync(sdk, mcp, command, expected, token);
    }
}
