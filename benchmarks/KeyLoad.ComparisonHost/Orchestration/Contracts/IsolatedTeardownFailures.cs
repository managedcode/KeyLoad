using System.Runtime.ExceptionServices;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.ComparisonHost.BenchmarkOrchestration;

internal sealed class IsolatedTeardownFailures(Exception? primary)
{
    private const int SingleItemCount = 1;
    private const int EmptyCount = 0;

    private const string Failure = "IsolatedTeardownFailed";
    private readonly List<Exception> cleanup = [];
    private readonly List<string> stages = [];
    private readonly List<(string Stage, Exception Failure)> observations = [];

    internal bool HasPrimary => primary is not null;

    internal void Record(string stage, Exception failure)
    {
        if (!observations.Any(observation => observation.Stage == stage && ReferenceEquals(observation.Failure, failure)))
        {
            observations.Add((stage, failure));
            stages.Add(stage);
        }
        if (!cleanup.Any(existing => ReferenceEquals(existing, failure)))
        {
            cleanup.Add(failure);
        }
    }

    internal byte[] CreateReceipt()
        => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = SingleItemCount,
            primaryFailure = HasPrimary,
            failedStages = stages.ToArray(),
        });

    internal void ThrowIfAny()
    {
        var failures = OrderedFailures();
        if (failures.Count == EmptyCount)
        {
            return;
        }

        var fatal = (FindFatal(failures)).Value;
        if (fatal is not null)
        {
            var prioritized = new List<Exception> { fatal };
            AddDistinct(prioritized, failures);
            Throw(prioritized);
        }
        Throw(failures);
    }

    private List<Exception> OrderedFailures()
    {
        var ordered = new List<Exception>();
        if (primary is not null)
        {
            ordered.Add(primary);
        }

        AddDistinct(ordered, cleanup);
        return ordered;
    }

    private static (bool HasValue, Exception? Value) FindFatal(IEnumerable<Exception> failures)
    {
        foreach (var failure in failures)
        {
            var fatal = CqrsRuntimeFailures.FindFatal(failure);
            if (fatal is not null)
            {
                return (true, ((Exception?)(fatal)));
            }
        }
        return (false, default(Exception?));
    }

    private static void AddDistinct(List<Exception> target, IEnumerable<Exception> additions)
    {
        foreach (var addition in additions)
        {
            if (!target.Any(existing => ReferenceEquals(existing, addition)))
            {
                target.Add(addition);
            }
        }
    }

    private static void Throw(List<Exception> failures)
    {
        if (failures.Count == SingleItemCount)
        {
            ExceptionDispatchInfo.Capture(failures[EmptyCount]).Throw();
        }

        throw new AggregateException(Failure, failures);
    }
}
