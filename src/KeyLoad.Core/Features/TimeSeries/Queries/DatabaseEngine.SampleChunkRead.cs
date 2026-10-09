
namespace KeyLoad.Core;

public sealed partial class DatabaseEngine
{
    /// <summary>Reads the complete authorized enrolled chunk window at the original native owner cut.</summary>
    public SampleChunkWindowResult ReadSampleChunkWindow(string principalId, ReadSampleChunkWindowRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var budget = new ReadExecutionBudget(OperationLimitsOptions, Clock, cancellationToken);
        budget.Check();
        return Store.Read(original =>
        {
            var grant = budget.CreateReadGrant(Limits.MaxQueryReadBytes, Limits.MaxScanRecords);
            var view = budget.CreateView(original, grant);
            return Features.TimeSeries.SampleChunkWindowReader.Read(this, view, principalId, request,
                budget, TimeSeriesOptions, Store.Position);
        });
    }
}
