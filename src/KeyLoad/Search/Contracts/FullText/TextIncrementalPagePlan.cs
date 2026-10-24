namespace KeyLoad.Server.Search;

internal sealed record TextIncrementalPagePlan(TextIncrementalChange[] Changes,
    TextIncrementalRecord[] Records, ulong NextRecord);
