namespace KeyLoad.Server.Search;

internal sealed record TextIncrementalSeedPlan(TextIncrementalRecord[] Records,
    TextIncrementalPosting[][] Postings, ulong NextRecord);
