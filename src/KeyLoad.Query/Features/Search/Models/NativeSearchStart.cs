namespace KeyLoad.Query.Features.Search;

internal sealed record NativeSearchStart(ICapturedTextRead? Captured, TextRanker? Ranker, RankedDocument[]? Completed);
