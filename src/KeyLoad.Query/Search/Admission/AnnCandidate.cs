namespace KeyLoad.Query.Features.Search;

internal readonly record struct AnnCandidate(int SourceOrdinal, string DocumentId, long DocumentRevision, double Score);
