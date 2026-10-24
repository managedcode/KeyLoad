namespace KeyLoad.Query.Search;

internal readonly record struct AnnCandidate(int SourceOrdinal, string DocumentId, long DocumentRevision, double Score);
