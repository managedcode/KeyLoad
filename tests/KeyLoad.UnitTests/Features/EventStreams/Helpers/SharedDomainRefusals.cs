using KeyLoad.Core;
using KeyLoad.CrashHost.Features.EventStreams;
using static KeyLoad.CrashHost.Features.EventStreams.SharedDomainProtocol;

namespace KeyLoad.UnitTests.Features.EventStreams;

internal static class SharedDomainRefusals
{
    internal static async Task ProducersAsync(DatabaseEngine database, SharedDomainState state, CancellationToken token)
    {
        var first = state.Scopes[FirstIndex];
        var second = state.Scopes[SecondIndex];
        var request = SharedDomainCorpus.Producer(first, Guid.NewGuid(), Refused);
        request = request with
        {
            Mutations = [.. request.Mutations.OfType<Mutation>().Where(mutation => mutation is not EnqueueMessage),
            new EnqueueMessage(second.Queue!, Refused, first.Payload, Headers)]
        };
        await SharedDomainRefusalImage.RequireAsync(database, state, first.Partition,
            SharedDomainOperations.Operation(OperationKind.Batch, request.CommandId, request), token);
        var document = state.Scopes[DocumentScopeIndex];
        var events = state.Scopes[EventScopeIndex];
        var isolated = new CommandRequest(Guid.NewGuid(), document.Partition,
            [new PutDocument(document.Documents!, Refused, document.Payload, NoRevision),
             new AppendEvents(events.Streams!, Refused, [new(Refused, EventType, events.Payload, Headers)], ExpectedStreamRevision.NoStream)]);
        await SharedDomainRefusalImage.RequireAsync(database, state, document.Partition,
            SharedDomainOperations.Operation(OperationKind.Batch, isolated.CommandId, isolated), token);
        await SharedDomainModelAssertions.AllAsync(database, state, token);
    }

    internal static async Task ConsumerAsync(DatabaseEngine database, SharedDomainState state, ProcessingRequest original, CancellationToken token)
    {
        var first = state.Scopes[FirstIndex];
        var second = state.Scopes[SecondIndex];
        var refused = original with
        {
            CommandId = Guid.NewGuid(),
            Effects =
            [new PutDocument(first.Documents!, Refused, first.Payload, NoRevision),
             new AppendEvents(first.Streams!, Refused, [new(Refused, EventType, first.Payload, Headers)], ExpectedStreamRevision.NoStream),
             new EnqueueMessage(second.Queue!, Refused, second.Payload, Headers)]
        };
        await SharedDomainRefusalImage.RequireAsync(database, state, first.Partition,
            SharedDomainOperations.Operation(OperationKind.Processing, refused.CommandId, refused), token);
    }
}
