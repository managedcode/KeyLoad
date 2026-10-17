namespace KeyLoad.Server.Features.Search;

internal sealed record NativeTextIncrementalPagePlan(NativeTextIncrementalChange[] Changes,
    NativeTextIncrementalRecord[] Records, ulong NextRecord);
