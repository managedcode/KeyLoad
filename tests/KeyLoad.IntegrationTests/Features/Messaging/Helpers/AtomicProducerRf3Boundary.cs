using KeyLoad.Client;
using KeyLoad.IntegrationTests.Features.ClientApi;

namespace KeyLoad.IntegrationTests.Features.Messaging;

internal static class AtomicProducerRf3Boundary
{
    internal static async Task RefuseAsync(KeyLoadClient sdk, McpOfficialClient mcp, QueueProducerRf3Seed seed, CancellationToken token)
    {
        var eventConflict = new CommandRequest(Guid.NewGuid(), seed.Lane.Partition,
            [new PutDocument(QueueProducerRf3Protocol.Collection, QueueProducerRf3Protocol.Refused, QueueProducerRf3Protocol.HealthyPayload),
             QueueProducerRf3Setup.Event(QueueProducerRf3Protocol.Original, QueueProducerRf3Protocol.HealthyPayload),
             new EnqueueMessage(seed.Lane.Queue, QueueProducerRf3Protocol.Refused, QueueProducerRf3Protocol.HealthyPayload)]);
        await RefusedAsync(sdk, mcp, eventConflict, ErrorCode.RevisionConflict, token);
        await QueueProducerRf3Assertions.AbsentAsync(sdk, seed, QueueProducerRf3Protocol.Refused, QueueProducerRf3Protocol.Refused, token);
        var foreign = eventConflict with
        {
            CommandId = Guid.NewGuid(),
            Mutations =
            [new PutDocument(QueueProducerRf3Protocol.Collection, QueueProducerRf3Protocol.Refused, QueueProducerRf3Protocol.HealthyPayload),
             new AppendEvents(QueueProducerRf3Protocol.ForeignStreamSet, QueueProducerRf3Protocol.Refused,
                 [new(QueueProducerRf3Protocol.Refused, QueueProducerRf3Protocol.EventType, QueueProducerRf3Protocol.HealthyPayload)], ExpectedStreamRevision.NoStream),
             new EnqueueMessage(seed.Lane.Queue, QueueProducerRf3Protocol.Refused, QueueProducerRf3Protocol.HealthyPayload)]
        };
        await RefusedAsync(sdk, mcp, foreign, ErrorCode.Conflict, token);
        var partition = seed.Lane.Partition with { TransactionDomainId = QueueProducerRf3Protocol.ForeignDomain };
        var page = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadStreamAsync(new(new(partition,
            QueueProducerRf3Protocol.ForeignStreamSet, QueueProducerRf3Protocol.Refused)), token));
        await Assert.That(page.Stream).IsEqualTo(new StreamRef(partition, QueueProducerRf3Protocol.ForeignStreamSet, QueueProducerRf3Protocol.Refused));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Events).IsEmpty();
        await QueueProducerRf3Assertions.EqualAsync(page.Head, new StreamHead(QueueProducerRf3Protocol.EmptyStreamRevision,
            QueueProducerRf3Protocol.FirstAvailableEventRevision, QueueProducerRf3Protocol.InitialEventGeneration));
        await QueueProducerRf3Assertions.AbsentAsync(sdk, seed, QueueProducerRf3Protocol.Refused, QueueProducerRf3Protocol.Refused, token);
    }

    private static async Task RefusedAsync(KeyLoadClient sdk, McpOfficialClient mcp, CommandRequest command,
        ErrorCode expected, CancellationToken token)
    {
        await QueueProducerRf3Assertions.DeniedAsync(await sdk.CommitAsync(command, token), expected);
        await McpCallerAssertions.ErrorAsync(await mcp.CallAsync(McpCallerTools.DocumentsCommit, command, token), expected, true);
        await AtomicProducerRf3Routes.RefusedAsync(sdk, mcp, command, expected, token);
    }
}
