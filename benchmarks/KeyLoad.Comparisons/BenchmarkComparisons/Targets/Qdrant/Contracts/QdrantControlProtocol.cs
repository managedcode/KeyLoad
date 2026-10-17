namespace KeyLoad.Comparisons.Targets;

internal static class QdrantControlProtocol
{
    internal const int NoAdditionalClient = 0;
    internal const int OneAdditionalClient = 1;
    internal const string DuplicateClientOwnership = "QdrantDuplicateClientOwnership";
    internal const string UnsupportedDocumentCorpus = "This target does not support the bounded scaled document corpus.";
    internal const string UnverifiedTopology = "unverified native topology";
    internal const string InitialWriteContract = "wait=true seed upserts; pre-timing copy verification";
    internal const string InitialReadContract = "exact=true query; seeded collection static during queries";
    internal const string Authentication = "Aspire API key; no row/field policy";
}
