namespace KeyLoad.Query.Features.Search;

internal sealed record SearchStart(ICapturedTextRead? Captured, TextRanker? Ranker, RankedDocument[]? Completed);
