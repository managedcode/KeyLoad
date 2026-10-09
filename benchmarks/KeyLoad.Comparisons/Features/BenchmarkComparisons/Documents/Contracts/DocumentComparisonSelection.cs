namespace KeyLoad.Comparisons;

/// <summary>Validated exact document-v1 workload selection.</summary>
public sealed record DocumentComparisonSelection(DocumentComparisonScenario Scenario, int DatasetRecords, int Clients)
{
    /// <summary>Validates canonical workload identities before acquiring resources.</summary>
    public void Validate()
    {
        var contract = DocumentComparisonContract.Current;
        if (!Enum.IsDefined(Scenario) || !contract.DatasetSizes.Contains(DatasetRecords)
            || (Scenario == DocumentComparisonScenario.Ingest
                ? DatasetRecords != contract.Ingestion.Records || !contract.Ingestion.Clients.Contains(Clients)
                : Clients != contract.Clients))
        {
            throw new ArgumentOutOfRangeException(nameof(DocumentComparisonSelection));
        }
    }
    /// <summary>Gets the declared operation count, distinct from dataset size and client count.</summary>
    public int Operations => Scenario == DocumentComparisonScenario.Ingest ? DocumentComparisonContract.Current.Ingestion.Records : DocumentComparisonContract.Current.Operations;
}
