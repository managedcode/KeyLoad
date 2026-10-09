
namespace KeyLoad.Comparisons;

internal sealed class DocumentInitializationTiming
{
    internal double? LoadSeconds { get; private set; }
    internal double? IndexBuildSeconds { get; private set; }
    internal bool IndexBuildApplicable { get; private set; } = true;
    internal bool Complete => LoadSeconds.HasValue && IndexBuildSeconds.HasValue;
    internal IDisposable MeasureLoad() => new Measurement(value => LoadSeconds = (LoadSeconds ?? DocumentMeasurementValues.NoObservedItems) + value);
    internal IDisposable MeasureIndex() => new Measurement(value => IndexBuildSeconds = (IndexBuildSeconds ?? DocumentMeasurementValues.NoObservedItems) + value);
    internal void IndexNotApplicable()
    {
        IndexBuildApplicable = false;
        IndexBuildSeconds = DocumentMeasurementValues.NoObservedItems;
    }
    private sealed class Measurement(Action<double> record) : IDisposable
    {
        private readonly long started = TimeProvider.System.GetTimestamp();
        private bool disposed;
        public void Dispose()
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            record(TimeProvider.System.GetElapsedTime(started).TotalSeconds);
        }
    }
}
