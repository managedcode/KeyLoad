namespace KeyLoad.Comparisons;

internal static class DocumentNativeInitialization
{
    internal static Task InitializeAsync(IComparisonTarget target, DocumentComparisonInitialization initialization, CancellationToken token)
    {
        Validate(initialization);
        return target.InitializeAsync(initialization.Corpus, token);
    }
    internal static void Validate(DocumentComparisonInitialization initialization)
    {
        ArgumentNullException.ThrowIfNull(initialization);
        if (initialization.Corpus is not DocumentComparisonCorpus || (!initialization.Seed && initialization.Corpus.Documents.Count != DocumentMeasurementValues.NoObservedItems)
            || initialization.MaximumIdentityExclusive < initialization.Corpus.Documents.Count
            || initialization.MaximumIdentityExclusive > DocumentComparisonContract.Current.Ingestion.Records + DocumentComparisonContract.Current.Operations)
        {
            throw new ArgumentOutOfRangeException(nameof(initialization));
        }
    }
}
