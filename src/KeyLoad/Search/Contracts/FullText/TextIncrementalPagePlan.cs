namespace KeyLoad.Server.Features.Search;

internal sealed record TextIncrementalPagePlan(TextIncrementalChange[] Changes,
    TextIncrementalRecord[] Records, ulong NextRecord);
