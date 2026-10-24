using KeyLoad.Client;
using KeyLoad.IntegrationTests.ClientApi;
using KeyLoad.RecoveryTests.EventStreams;
using static KeyLoad.IntegrationTests.EventStreams.SharedDomainPublicProtocol;
using static KeyLoad.RecoveryTests.EventStreams.SharedDomainProtocol;

namespace KeyLoad.IntegrationTests.EventStreams;

internal static class SharedDomainPublicStreams
{
    internal static async Task RequireAsync(KeyLoadClient sdk, McpOfficialClient official,
        SharedDomainPublicState state, SharedDomainScope scope, CancellationToken token)
    {
        if (scope.Streams is not { } streams)
        {
            return;
        }
        var processed = state.Processed && scope.Partition.TransactionDomainId == FirstDomain;
        await OneAsync(sdk, official, state, scope, new(scope.Partition, streams, Item),
            processed ? SecondRevision : FirstRevision, FirstRevision, token);
        await OneAsync(sdk, official, state, scope, new(scope.Partition, streams, Healthy),
            state.HealthyWritten ? FirstRevision : NoRevision, processed ? AckVersion : SecondRevision, token);
        await OneAsync(sdk, official, state, scope, new(scope.Partition, streams, Refused), NoRevision, NoRevision, token);
    }

    private static async Task OneAsync(KeyLoadClient sdk, McpOfficialClient official, SharedDomainPublicState state,
        SharedDomainScope scope, StreamRef stream, long tail, long sequence, CancellationToken token)
    {
        var request = new ReadStreamRequest(stream, Limit: EventPageLimit);
        var first = await McpCallerAssertions.SdkSuccessAsync(await sdk.ReadStreamAsync(request, token));
        var second = (await McpCallerAssertions.SuccessAsync<StreamPage>(await official.CallAsync(
            McpCallerTools.StreamsRead, request, token))).Value;
        await ShapeAsync(state, scope, first, stream, tail, sequence);
        await ShapeAsync(state, scope, second, stream, tail, sequence);
        await SharedDomainPublicValues.EqualAsync(first.Events.ToArray(), second.Events.ToArray());
    }

    private static async Task ShapeAsync(SharedDomainPublicState state, SharedDomainScope scope, StreamPage page,
        StreamRef stream, long tail, long sequence)
    {
        await Assert.That(page.Stream).IsEqualTo(stream);
        await Assert.That(page.Head).IsEqualTo(new StreamHead(tail, FirstRevision, Generation));
        await Assert.That(page.HasMore).IsFalse();
        await Assert.That(page.Cursor).IsNull();
        await Assert.That(page.SnapshotCutPosition).IsEqualTo(page.CutPosition);
        var minimum = state.Healthy.Concat(state.Originals).Where(item => item.Command.Partition == scope.Partition)
            .Select(item => item.Receipt.Token.Position).DefaultIfEmpty(NoRevision).Max();
        if (scope.Partition.TransactionDomainId == FirstDomain && state.ProcessingReceipt is { } processing)
        {
            minimum = Math.Max(minimum, processing.Token.Position);
        }
        await Assert.That(page.CutPosition).IsGreaterThanOrEqualTo(minimum);
        await Assert.That(page.Events.Length).IsEqualTo(checked((int)tail));
        await EventsAsync(state, scope, page, sequence);
    }

    private static async Task EventsAsync(SharedDomainPublicState state, SharedDomainScope scope, StreamPage page, long sequence)
    {
        var key = scope.Partition.AtomicPartitionId + IdentitySeparator + page.Stream.StreamId;
        state.RecordedEvents.TryGetValue(key, out var original);
        for (var index = FirstIndex; index < page.Events.Length; index++)
        {
            var actual = page.Events[index];
            var id = index == FirstIndex ? page.Stream.StreamId : Processed;
            await Assert.That(actual.RecordedAt).IsNotEqualTo(default(DateTimeOffset));
            await Assert.That(actual.Stream).IsEqualTo(page.Stream);
            await Assert.That(actual.Revision).IsEqualTo(index + FirstRevision);
            await Assert.That(actual.EventSequence).IsEqualTo(sequence + index);
            await SharedDomainPublicValues.EqualAsync(actual.Data, new EventData(id, EventType, scope.Payload, Headers));
        }
        if (original is not null)
        {
            await SharedDomainPublicValues.EqualAsync(page.Events.Take(original.Length).ToArray(), original);
        }
        state.RecordedEvents[key] = page.Events.ToArray();
    }
}
