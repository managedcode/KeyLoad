namespace KeyLoad.Comparisons;

internal static class VectorWorkloadExecutorValues
{
    internal const int FirstIndex = 0;
    internal const string TheVectorWorkloadDidNotComplete = "The vector workload did not complete its exact operation schedule.";
    internal const string MixedVectorSearchesAndUpdatesDid = "Mixed vector searches and updates did not overlap in both directions.";
    internal const string AggregateVectorRecallIsBelowThe = "Aggregate vector recall is below the frozen accuracy contract.";
    internal const int SingleElementOffset = 1;
}
