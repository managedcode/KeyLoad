namespace KeyLoad.Server.Features.Search;

internal sealed record TextIncrementalSeedPlan(TextIncrementalRecord[] Records,
    TextIncrementalPosting[][] Postings, ulong NextRecord);
