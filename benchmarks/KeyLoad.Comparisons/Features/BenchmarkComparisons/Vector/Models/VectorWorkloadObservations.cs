namespace KeyLoad.Comparisons;

internal sealed class VectorWorkloadObservations(VectorComparisonProfile profile)
{
    internal double[] Latencies { get; } = new double[profile.LatencySampleCount];
    internal double[] Recalls { get; } = new double[profile.MeasuredQueries];
    internal Exception? Failure;
    internal int NextQuery = -VectorWorkloadObservationsValues.SingleElementOffset;
    internal int QueryActive = VectorWorkloadObservationsValues.SingleElementOffset;
    internal int UpdateActive = profile.UpdateCount > VectorWorkloadObservationsValues.FirstIndex ? VectorWorkloadObservationsValues.SingleElementOffset : VectorWorkloadObservationsValues.FirstIndex;
    internal int QueriesDuringUpdates;
    internal int UpdatesDuringQueries;
    internal int QuerySuccesses;
    internal int UpdateAttempts;
    internal int UpdateSuccesses;
    internal double QuerySeconds { get; set; }
    internal double UpdateSeconds { get; set; }
}
