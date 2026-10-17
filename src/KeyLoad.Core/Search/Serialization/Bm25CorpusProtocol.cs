namespace KeyLoad.Core.Features.Search;

internal static class Bm25CorpusProtocol
{
    internal const int Format = 1;
    internal const string CurrentFamily = "bm25-corpus-current";
    internal const string CurrentAlias = "keyload.core.search.bm25-corpus-current.v1";
    internal const string PhaseAlias = "keyload.core.search.bm25-corpus-publication-phase.v1";
    internal const string NativePurpose = "keyload-bm25-corpus-publication-verified-v1";
    internal const string DigestDomain = "keyload-bm25-corpus-source-v1";
    internal const string Invalid = "The BM25 corpus statistics are inconsistent.";
    internal const string Changed = "The BM25 corpus source or current generation changed.";
    internal const string Capacity = "The BM25 corpus statistics exceed the configured family budget.";
    internal const string Unavailable = "The BM25 corpus maintenance operation could not complete.";
    internal const string Administrator = "BM25 corpus maintenance requires current administrator authority.";
}
