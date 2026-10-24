namespace KeyLoad.Query.Search;

internal sealed record SearchStart(ICapturedTextRead? Captured, TextRanker? Ranker, RankedDocument[]? Completed);
