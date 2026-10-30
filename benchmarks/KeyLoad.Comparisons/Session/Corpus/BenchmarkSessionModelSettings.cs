namespace KeyLoad.Comparisons;

internal sealed record BenchmarkSessionModelSettings(BenchmarkSessionOptions Session) : IComparisonSettings
{
    private const int ModelDimensions = 32, ModelTopK = 10, ModelPayloadBytes = 1024, MeasurementPasses = 2;
    public int Documents => Session.Records;
    public int Operations => Session.Operations;
    public int Concurrency => Session.Concurrency;
    public int Repetitions => MeasurementPasses;
    public int Dimensions => ModelDimensions;
    public int TopK => Math.Min(ModelTopK, Session.Records);
    public int PayloadBytes => ModelPayloadBytes;
    public int Seed => BenchmarkSessionLimits.CorpusSeed;
    public int TimeoutSeconds => Session.TimeoutSeconds;
    public int GraphVertices => BenchmarkSessionLimits.NoObservedItems;
    public int GraphFanOut => BenchmarkSessionLimits.NoObservedItems;
    public int GraphDepth => BenchmarkSessionLimits.NoObservedItems;
}
