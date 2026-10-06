using System.Text;

namespace KeyLoad.Comparisons;

internal sealed class ScaledMeasurementState(int operations, int sampleCapacity)
{
    private const int MissingItemIndex = -1;

    private readonly int[] sampleIndices = ScaledLatencySample.Indices(operations, sampleCapacity);
    private readonly OperationSample?[] samples = new OperationSample[sampleCapacity];
    private int next = MissingItemIndex;
    private int attempted;
    private int successes;
    private int failures;
    private int timeouts;
    private int rejections;

    internal int Next() => Interlocked.Increment(ref next);
    internal void StartOperation() => Interlocked.Increment(ref attempted);

    internal void CompleteOperation(int operation, int worker, double started, double completed,
        BenchmarkDocument input, string? error, bool success, bool timeout, bool rejection = false)
    {
        const int NoObservedItems = 0;

        if (success)
        {
            Interlocked.Increment(ref successes);
        }
        else
        {
            Interlocked.Increment(ref failures);
        }
        if (timeout)
        {
            Interlocked.Increment(ref timeouts);
        }
        if (rejection)
        {
            Interlocked.Increment(ref rejections);
        }
        var sample = Array.BinarySearch(sampleIndices, operation);
        if (sample >= NoObservedItems)
        {
            samples[sample] = new(operation, worker, started, completed, success, error,
                Encoding.UTF8.GetByteCount(input.Json), null, null);
        }
    }

    internal ScaledMeasurementResult Complete(double elapsedSeconds, ClientResources resources)
        => new(samples.Where(sample => sample is not null).Select(sample => sample!).ToArray(), elapsedSeconds,
            Volatile.Read(ref attempted), Volatile.Read(ref successes), Volatile.Read(ref failures),
            Volatile.Read(ref timeouts), Volatile.Read(ref rejections), resources);
}

internal sealed record ScaledMeasurementResult(OperationSample[] Samples, double ElapsedSeconds, int Attempted,
    int Successes, int Failures, int DeadlineTimeouts, int Rejections, ClientResources ClientResources);

internal sealed class ScaledOperationInputs(IComparisonCorpus corpus, Scenario scenario)
{
    private const int NoObservedItems = 0;

    internal Scenario Scenario => scenario;
    internal int PayloadBytes => corpus.Settings.PayloadBytes;
    internal BenchmarkDocument Create(int operation, bool warmup) => corpus.Input(scenario, NoObservedItems, operation, warmup);
}

internal static class ScaledLatencySample
{
    internal static int[] Indices(int operations, int capacity)
    {
        const int SingleItemCount = 1;
        const int FirstElementIndex = 0;
        const int AdjacentElementOffset = 1;

        if (operations < SingleItemCount || capacity < SingleItemCount || capacity > operations)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity));
        }
        if (capacity == SingleItemCount)
        {
            return [FirstElementIndex];
        }
        var indices = new int[capacity];
        for (var index = FirstElementIndex; index < capacity; index++)
        {
            indices[index] = checked((int)((long)index * (operations - AdjacentElementOffset) / (capacity - AdjacentElementOffset)));
        }
        return indices;
    }
}
