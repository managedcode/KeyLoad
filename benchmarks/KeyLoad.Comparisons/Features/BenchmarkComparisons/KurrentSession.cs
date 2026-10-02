using System.Text;
using KurrentDB.Client;

namespace KeyLoad.Comparisons.Targets;

internal sealed class KurrentSession(KurrentDBClient client, KurrentTarget target) : IComparisonSession
{
    public async Task<FoundEvent?> ReadEventAsync(BenchmarkDocument document, CancellationToken cancellationToken)
    {
        var stream = target.StreamName(document);
        var result = client.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start,
            maxCount: KurrentConstants.ReadLimit, cancellationToken: cancellationToken);
        if (await result.ReadState == ReadState.StreamNotFound)
        {
            throw new ComparisonFailureException(KurrentConstants.ReadCardinality);
        }
        var items = new List<ResolvedEvent>(KurrentConstants.ReadLimit);
        await foreach (var item in result.WithCancellation(cancellationToken))
        {
            items.Add(item);
            if (items.Count == KurrentConstants.ReadLimit)
            {
                break;
            }
        }
        if (items.Count != KurrentConstants.ExpectedSingleEventCount)
        {
            throw new ComparisonFailureException(KurrentConstants.ReadCardinality);
        }
        var resolvedItem = items[0];
        var actual = resolvedItem.OriginalEvent;
        var revision = resolvedItem.OriginalEventNumber.ToUInt64();
        var canonicalRevision = checked(revision + (ulong)KurrentConstants.CanonicalFirstRevision);
        var found = new FoundEvent(actual.EventId.ToGuid(), canonicalRevision, Encoding.UTF8.GetString(actual.Data.Span));
        return found;
    }

    public async Task<OperationResult> ExecuteAsync(Scenario scenario, BenchmarkDocument document,
        CancellationToken cancellationToken)
    {
        switch (scenario)
        {
            case Scenario.StreamAppend:
                var appendStream = target.StreamName(document);
                target.TrackStream(appendStream);
                await client.AppendToStreamAsync(appendStream, StreamState.NoStream,
                    [KurrentTarget.CreateEvent(document)], cancellationToken: cancellationToken);
                return new();
            case Scenario.StreamRead:
                return new(Event: await ReadEventAsync(document, cancellationToken));
            default:
                throw new ComparisonFailureException(KurrentConstants.UnsupportedScenario);
        }
    }

    public Task<FoundDocument?> ReadAsync(BenchmarkDocument document, CancellationToken cancellationToken)
        => throw new NotSupportedException(KurrentConstants.UnsupportedScenario);

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
