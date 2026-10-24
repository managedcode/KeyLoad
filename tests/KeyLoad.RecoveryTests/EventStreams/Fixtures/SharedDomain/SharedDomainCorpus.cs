using System.Collections.Immutable;
using static KeyLoad.RecoveryTests.EventStreams.SharedDomainProtocol;

namespace KeyLoad.RecoveryTests.EventStreams;

internal static class SharedDomainCorpus
{
    internal static ImmutableArray<SharedDomainScope> Create(string tenant, string database)
        => [new(new(tenant, database, FirstDomain, LiteralKey), FirstDocuments, FirstStreams, FirstQueue, FirstPayload),
            new(new(tenant, database, SecondDomain, LiteralKey), SecondDocuments, SecondStreams, SecondQueue, SecondPayload),
            new(new(tenant, database, IsolatedDocuments, LiteralKey), IsolatedDocuments, null, null, IsolatedDocumentPayload),
            new(new(tenant, database, IsolatedStreams, LiteralKey), null, IsolatedStreams, null, IsolatedEventPayload),
            new(new(tenant, database, IsolatedQueue, LiteralKey), null, null, IsolatedQueue, IsolatedQueuePayload)];

    internal static CommandRequest Producer(SharedDomainScope scope, Guid id, string item)
    {
        var effects = ImmutableArray.CreateBuilder<Mutation>();
        if (scope.Documents is { } documents)
        {
            effects.Add(new PutDocument(documents, item, scope.Payload, NoRevision));
        }
        if (scope.Streams is { } streams)
        {
            effects.Add(new AppendEvents(streams, item, [new(item, EventType, scope.Payload, Headers)], ExpectedStreamRevision.NoStream));
        }
        if (scope.Queue is { } queue)
        {
            effects.Add(new EnqueueMessage(queue, item, scope.Payload, Headers));
        }
        return new(id, scope.Partition, effects.ToImmutable());
    }

    internal static ProcessingRequest Processing(SharedDomainScope scope, string deliveryToken, Guid id)
        => new(id, new(scope.Partition, scope.Queue!), deliveryToken, Handler, Generation,
            [new PutDocument(scope.Documents!, Processed, scope.Payload, NoRevision),
             new AppendEvents(scope.Streams!, Item, [new(Processed, EventType, scope.Payload, Headers)],
                 ExpectedStreamRevision.Exact(FirstRevision)),
             new EnqueueMessage(scope.Queue!, Processed, scope.Payload, Headers)]);
}
