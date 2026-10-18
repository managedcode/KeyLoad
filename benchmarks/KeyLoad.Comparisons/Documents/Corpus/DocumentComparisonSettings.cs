namespace KeyLoad.Comparisons;

internal sealed record DocumentComparisonSettings(int Documents, int Concurrency) : IComparisonSettings
{
    public int Seed => DocumentComparisonContract.Current.Seed;
    public int Operations => DocumentComparisonContract.Current.Operations;
    public int Warmup => DocumentComparisonContract.Current.Warmup;
    public int Repetitions => DocumentComparisonContract.Current.Repetitions;
    public int PayloadBytes => DocumentComparisonContract.Current.PayloadBytes;
    public int Dimensions => DocumentMeasurementValues.DocumentSchemaDimensions;
    public int TopK => DocumentMeasurementValues.DefaultUnusedTopK;
    public int TimeoutSeconds => DocumentComparisonContract.Current.TimeoutSeconds;
    public int GraphVertices => DocumentMeasurementValues.NoObservedItems;
    public int GraphFanOut => DocumentMeasurementValues.NoObservedItems;
    public int GraphDepth => DocumentMeasurementValues.NoObservedItems;
}
