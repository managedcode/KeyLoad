using System.Runtime.ExceptionServices;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal sealed class IsolatedNativeTeardownFailures(Exception? primary)
{
    private const string Failure = "IsolatedNativeTeardownFailed";
    private readonly List<Exception> cleanup = [];
    private readonly List<string> stages = [];

    internal bool HasPrimary => primary is not null;

    internal void Record(string stage, Exception failure)
    {
        stages.Add(stage);
        if (!cleanup.Any(existing => ReferenceEquals(existing, failure))) cleanup.Add(failure);
    }

    internal byte[] CreateReceipt()
        => System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new
        {
            schemaVersion = 1,
            primaryFailure = HasPrimary,
            failedStages = stages.ToArray(),
        });

    internal void ThrowIfAny()
    {
        var failures = OrderedFailures();
        if (failures.Count == 0) return;
        var fatal = FindFatal(failures);
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
        if (primary is not null) ordered.Add(primary);
        AddDistinct(ordered, cleanup);
        return ordered;
    }

    private static Exception? FindFatal(IEnumerable<Exception> failures)
    {
        foreach (var failure in failures)
        {
            var fatal = CqrsRuntimeFailures.FindFatal(failure);
            if (fatal is not null) return fatal;
        }
        return null;
    }

    private static void AddDistinct(List<Exception> target, IEnumerable<Exception> additions)
    {
        foreach (var addition in additions)
        {
            if (!target.Any(existing => ReferenceEquals(existing, addition))) target.Add(addition);
        }
    }

    private static void Throw(List<Exception> failures)
    {
        if (failures.Count == 1) ExceptionDispatchInfo.Capture(failures[0]).Throw();
        throw new AggregateException(Failure, failures);
    }
}
