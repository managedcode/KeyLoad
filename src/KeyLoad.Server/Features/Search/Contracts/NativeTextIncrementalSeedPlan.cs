namespace KeyLoad.Server.Features.Search;

internal sealed record NativeTextIncrementalSeedPlan(NativeTextIncrementalRecord[] Records,
    NativeTextIncrementalPosting[][] Postings, ulong NextRecord);
