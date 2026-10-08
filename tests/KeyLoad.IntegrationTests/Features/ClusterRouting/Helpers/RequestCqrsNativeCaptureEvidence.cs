namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal sealed class RequestCqrsNativeCaptureEvidence
{
    private static readonly TimeProvider ObservationClock = TimeProvider.System;
    private int lines;
    private int candidates;
    private int accepted;
    private int malformed;
    private int oversized;
    private bool saturated;
    private DateTimeOffset? first;
    private DateTimeOffset? last;

    internal void Observe(string line, bool parsed)
    {
        var now = ObservationClock.GetUtcNow();
        first ??= now;
        last = now;
        Increment(ref lines);
        if (line.Length > RequestCqrsRf3McpRejectionNodeCapture.MaximumLineCharacters)
        { Increment(ref oversized); return; }
        if (line.Contains(RequestCqrsRf3McpRejectionNodeCapture.MessagePrefix, StringComparison.Ordinal))
        {
            Increment(ref candidates);
            if (!parsed)
            { Increment(ref malformed); }
        }
        if (parsed)
        { Increment(ref accepted); }
    }

    internal RequestCqrsNativeCaptureSnapshot Snapshot(string node)
        => new(node, lines, candidates, accepted, malformed, oversized, saturated, first, last);

    private void Increment(ref int count)
    {
        if (count == int.MaxValue)
        { saturated = true; }
        else
        { count++; }
    }
}

internal sealed record RequestCqrsNativeCaptureSnapshot(string Node, int Lines, int Candidates,
    int Accepted, int Malformed, int Oversized, bool Saturated,
    DateTimeOffset? FirstLineObservedAt, DateTimeOffset? LastLineObservedAt);
