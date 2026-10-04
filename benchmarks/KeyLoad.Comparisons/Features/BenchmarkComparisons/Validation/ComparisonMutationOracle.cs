namespace KeyLoad.Comparisons;

internal static class ComparisonMutationOracle
{
    private const string UpdateMismatch = "UpdateReadbackMismatch";
    private const string DeleteMismatch = "DeleteReadbackMismatch";

    internal static void RequireFinalState(Scenario scenario, FoundDocument? actual, BenchmarkDocument expected)
    {
        if (scenario == Scenario.DocumentUpdate)
        {
            if (!BenchmarkDataset.SameDocument(actual, expected))
            {
                throw new ComparisonFailureException(UpdateMismatch);
            }
        }
        else if (scenario == Scenario.DocumentDelete)
        {
            if (actual is not null)
            {
                throw new ComparisonFailureException(DeleteMismatch);
            }
        }
        else
        {
            throw new ArgumentOutOfRangeException(nameof(scenario));
        }
    }
}
