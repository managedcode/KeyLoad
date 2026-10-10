namespace KeyLoad.Core;

internal sealed record EventVectorStreamHeadCapture(string StreamId, StreamHead Head,
    EventVectorCoverageRow OriginalRow);
