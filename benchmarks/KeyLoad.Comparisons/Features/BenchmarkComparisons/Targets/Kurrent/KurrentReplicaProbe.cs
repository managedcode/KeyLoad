using KurrentDB.Client;
using Microsoft.Extensions.Options;
using KurrentEventData = KurrentDB.Client.EventData;

namespace KeyLoad.Comparisons.Targets;

internal readonly record struct KurrentAppendCut(long CommitPosition, long PreparePosition);

internal static class KurrentReplicaProbe
{
    public static async Task<ClusterEvidence> VerifyCopyAsync(KurrentDBClient writer, KurrentDBClient[] nodeClients,
        HttpClient[] httpClients, ComparisonTopology topology, string stream, KurrentEventData eventData,
        KurrentStreamOwnership ownership, TimeSpan timeout,
        CancellationToken cancellationToken, IOptions<ComparisonLifecycleOptions> options)
    {
        var cut = await AppendAndCaptureCutAsync(writer, ownership, stream, eventData, cancellationToken);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            while (true)
            {
                var evidence = await TryCreateCopyEvidenceAsync(nodeClients, httpClients, topology, stream,
                    eventData, cut, timeout, limit.Token, options);
                if (evidence is not null)
                {
                    return evidence;
                }
                await Task.Delay(options.Value.KurrentReadinessPollInterval, limit.Token);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ComparisonFailureException(KurrentConstants.CopyProbeFailed);
        }
    }

    private static async Task<ClusterEvidence?> TryCreateCopyEvidenceAsync(KurrentDBClient[] nodeClients,
        HttpClient[] httpClients, ComparisonTopology topology, string stream, KurrentEventData eventData,
        KurrentAppendCut cut, TimeSpan timeout, CancellationToken cancellationToken, IOptions<ComparisonLifecycleOptions> options)
    {
        var views = await KurrentClusterVerifier.ReadReadyViewsAsync(httpClients, topology, timeout, cancellationToken, options);
        if (!await AllCopiesAtCutAsync(nodeClients, views, stream, eventData, cut, cancellationToken))
        {
            return null;
        }

        var finalViews = await KurrentClusterVerifier.ReadReadyViewsAsync(httpClients, topology, timeout, cancellationToken, options);
        RequireSameLocalMembers(views, finalViews);
        return await AllCopiesAtCutAsync(nodeClients, finalViews, stream, eventData, cut, cancellationToken)
            ? KurrentClusterVerifier.CreateEvidence(finalViews, topology, cut)
            : null;
    }

    public static async Task RequireOriginalEventAsync(KurrentDBClient client, string stream, KurrentEventData expected,
        CancellationToken cancellationToken)
    {
        if (!await ReadExpectedEventAsync(client, stream, expected, cancellationToken))
        {
            throw new ComparisonFailureException(KurrentConstants.ReadCardinality);
        }
    }

    private static async Task<KurrentAppendCut> AppendAndCaptureCutAsync(KurrentDBClient writer,
        KurrentStreamOwnership ownership, string stream, KurrentEventData eventData, CancellationToken cancellationToken)
    {
        var result = await KurrentOwnedStreamAppend.AppendAsync(writer, ownership, stream, eventData, cancellationToken);
        var commit = checked((long)result.LogPosition.CommitPosition);
        var prepare = checked((long)result.LogPosition.PreparePosition);
        if (commit < prepare)
        {
            throw new ComparisonFailureException(KurrentConstants.ProbeProgressInvalid);
        }

        return new KurrentAppendCut(commit, prepare);
    }

    private static async Task<bool> AllCopiesAtCutAsync(KurrentDBClient[] clients, KurrentGossipView[] views,
        string stream, KurrentEventData expected, KurrentAppendCut cut, CancellationToken cancellationToken)
    {
        if (clients.Length != views.Length)
        {
            throw new ComparisonFailureException(KurrentConstants.InvalidTopology);
        }

        var copied = true;
        for (var index = 0; index < clients.Length; index++)
        {
            if (!CheckpointAtCut(views[index].LocalMember, cut))
            {
                copied = false;
            }

            if (!await ReadExpectedEventAsync(clients[index], stream, expected, cancellationToken))
            {
                copied = false;
            }
        }
        return copied;
    }

    private static bool CheckpointAtCut(KurrentGossipMember member, KurrentAppendCut cut)
    {
        var recordPosition = Math.Max(cut.CommitPosition, cut.PreparePosition);
        return member.Commit >= cut.CommitPosition && member.Writer > recordPosition && member.Chaser > recordPosition;
    }

    private static async Task<bool> ReadExpectedEventAsync(KurrentDBClient client, string stream, KurrentEventData expected,
        CancellationToken cancellationToken)
    {
        var result = client.ReadStreamAsync(Direction.Forwards, stream, StreamPosition.Start,
            maxCount: KurrentConstants.ReadLimit, cancellationToken: cancellationToken);
        if (await result.ReadState == ReadState.StreamNotFound)
        {
            return false;
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
        ValidateSingleEvent(items, expected);
        return items.Count == KurrentConstants.ExpectedSingleEventCount;
    }

    private static void ValidateSingleEvent(List<ResolvedEvent> items, KurrentEventData expected)
    {
        if (items.Count > KurrentConstants.ExpectedSingleEventCount)
        {
            throw new ComparisonFailureException(KurrentConstants.ReadCardinality);
        }

        if (items.Count == KurrentConstants.NoEventsRead)
        {
            return;
        }

        var actual = items[0].OriginalEvent;
        if (items[0].OriginalEventNumber.ToUInt64() != KurrentConstants.NativeFirstRevision ||
            actual.EventId != expected.EventId || !actual.Data.Span.SequenceEqual(expected.Data.Span))
        {
            throw new ComparisonFailureException(KurrentConstants.ProbeEventMismatch);
        }
    }

    private static void RequireSameLocalMembers(KurrentGossipView[] before, KurrentGossipView[] after)
    {
        if (before.Length != after.Length || before.Where((view, index) => view.LocalMember.Id != after[index].LocalMember.Id).Any() ||
            !ViewsHaveSameMembers(before[0], after[0]))
        {
            throw new ComparisonFailureException(KurrentConstants.ClusterMembership);
        }
    }

    private static bool ViewsHaveSameMembers(KurrentGossipView before, KurrentGossipView after)
    {
        var previous = before.Members.OrderBy(member => member.Id, StringComparer.Ordinal).ToArray();
        var current = after.Members.OrderBy(member => member.Id, StringComparer.Ordinal).ToArray();
        return previous.Length == current.Length && !previous.Where((member, index) =>
            member.Id != current[index].Id || member.State != current[index].State ||
            member.HttpEndpointIp != current[index].HttpEndpointIp || member.HttpEndpointPort != current[index].HttpEndpointPort).Any();
    }
}
