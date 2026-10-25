using System.Runtime.CompilerServices;
using KeyLoad.ClusterTests.TestInfrastructure;
using KeyLoad.Server;

namespace KeyLoad.ClusterTests.StorageReplication;

/// <summary>Opt-in failure evidence retention after every original owned cleanup stage has settled.</summary>
internal sealed class ClusterFixtureFailureRootRetention
{
    private static readonly ConditionalWeakTable<ClusterFixture, ClusterFixtureFailureRootRetention> Owners = new();
    private const int NoFailures = 0;
    private const string RetainedRoot = "Original Orleans failure evidence retained at ";
    private bool enabled;
    private bool retain;
    private Func<Task>? deferredRelease;

    internal static ClusterFixtureFailureRootRetention For(ClusterFixture fixture)
        => Owners.GetValue(fixture, static _ => new());

    internal void Enable() => enabled = true;

    internal void Retain() => retain = true;

    internal Task StartupFailureAsync(ClusterFixture fixture, Exception failure)
    {
        if (enabled)
        {
            retain = true;
        }
        return ClusterFixtureStartupFailure.DisposeAndThrowAsync(fixture, failure);
    }

    internal Task ReleaseAsync(Func<Task> release)
    {
        if (!enabled)
        {
            return release();
        }
        deferredRelease = release;
        return Task.CompletedTask;
    }

    internal async Task CompleteAsync(string root, Func<Task> complete,
        List<Exception> failures)
    {
        if (!enabled)
        {
            await complete().ConfigureAwait(false);
            return;
        }
        var completionFailures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(complete, completionFailures).ConfigureAwait(false);
        var release = deferredRelease;
        deferredRelease = null;
        foreach (var failure in completionFailures)
        {
            if (!Represented(failure, failures))
            {
                failures.Add(failure);
            }
        }
        if (!retain && release is not null && failures.Count == NoFailures)
        {
            await ServerFailureObserver.ObserveAsync(release, failures).ConfigureAwait(false);
        }
        if (Directory.Exists(root))
        {
            ServerFailureObserver.Observe(() => Console.Error.WriteLine(RetainedRoot + root), failures);
        }
        retain |= failures.Count != NoFailures;
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static bool Represented(Exception failure, List<Exception> failures)
    {
        if (failures.Any(original => ReferenceEquals(original, failure)))
        {
            return true;
        }
        return failure is AggregateException aggregate && aggregate.InnerExceptions.Count > NoFailures
            && aggregate.InnerExceptions.All(inner => failures.Any(original => ReferenceEquals(original, inner)));
    }
}
