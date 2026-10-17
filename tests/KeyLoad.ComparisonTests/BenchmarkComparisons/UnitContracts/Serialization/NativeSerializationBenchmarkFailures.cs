using System.Runtime.ExceptionServices;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class NativeSerializationBenchmarkFailures
{
    private const string MultipleFailuresMessage = "The native serialization benchmark process or cleanup failed.";
    private readonly List<Exception> _items = [];

    internal static bool IsNonFatal(Exception failure)
        => failure is not (OutOfMemoryException or StackOverflowException or AccessViolationException);

    internal void Add(Exception failure)
    {
        if (!_items.Any(existing => ReferenceEquals(existing, failure)))
        {
            _items.Add(failure);
        }
    }

    internal void AddTaskFailures(Task task, Exception observed)
    {
        if (task.Exception is { } aggregate)
        {
            foreach (var failure in aggregate.InnerExceptions)
            {
                Add(failure);
            }
        }
        else
        {
            Add(observed);
        }
    }

    internal void ThrowIfAny()
    {
        if (_items.Count == 1)
        {
            ExceptionDispatchInfo.Capture(_items[0]).Throw();
        }
        else if (_items.Count > 1)
        {
            throw new AggregateException(MultipleFailuresMessage, _items);
        }
    }
}
