using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.Options;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Retains bounded, privacy-safe Aspire lifecycle evidence for failed comparison startup.</summary>
internal sealed class ComparisonResourceDiagnostics(ResourceNotificationService notifications, IOptions<NativeComparisonHarnessOptions> executionOptions) : IAsyncDisposable
{
    private const string Comparisons = "comparisons";
    private const string Postgres = "benchmark-postgres";
    private const string Rabbit = "benchmark-rabbit";
    private const string Redis = "benchmark-redis";
    private const string Qdrant = "benchmark-qdrant";
    private const string Neo4j = "benchmark-neo4j";
    private const string Node1 = "node1";
    private const string Node2 = "node2";
    private const string Node3 = "node3";

    private static readonly string[] ResourceNames =
    [
        Comparisons, Postgres, Rabbit, Redis, Qdrant, Neo4j, Node1, Node2, Node3
    ];

    private readonly CancellationTokenSource lifetime = new();
    private readonly ComparisonLifecycleRecord?[] records = new ComparisonLifecycleRecord?[executionOptions.Value.MaximumResourceDiagnosticRecords];
    private readonly System.Threading.Lock recordsGate = new();
    private readonly System.Threading.Lock lifecycleGate = new();
    private Task? capture;
    private Task? disposal;
    private int nextRecord;
    private int retainedRecords;
    private long observerSequence;
    private bool disposed;
    private int captureFaulted;
    private int cancelFaulted;

    /// <summary>Starts observing lifecycle updates before the AppHost starts.</summary>
    public void Start()
    {
        lock (lifecycleGate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            capture ??= CaptureAsync();
        }
    }

    /// <summary>Writes bounded lifecycle evidence to stderr without propagating diagnostic faults.</summary>
    public Task WriteFailureAsync() => WriteFailureSafelyAsync();

    /// <summary>Cancels and observes the native notification collector.</summary>
    public ValueTask DisposeAsync()
    {
        lock (lifecycleGate)
        {
            disposed = true;
            var task = disposal ??= DisposeSafelyAsync(capture);
            return new ValueTask(task);
        }
    }

    private async Task CaptureAsync()
    {
        await foreach (var change in notifications.WatchAsync(lifetime.Token))
        {
            var name = change.Resource.Name;
            if (Array.IndexOf(ResourceNames, name) >= 0)
            {
                var sequence = Interlocked.Increment(ref observerSequence);
                Retain(ComparisonLifecycleRecord.FromNative(name, change.Snapshot, sequence));
            }
        }
    }

    private async Task WriteFailureSafelyAsync()
    {
        var output = WriteFailureCoreAsync();
        await output.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (output.IsFaulted)
        {
            Volatile.Write(ref outputFaulted, 1);
        }
    }

    private async Task WriteFailureCoreAsync()
    {
        var output = CreateFailureOutput();
        var write = Console.Error.WriteAsync(output);
        await write.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (write.IsFaulted)
        {
            Volatile.Write(ref outputFaulted, 1);
        }
    }

    private async Task DisposeSafelyAsync(Task? collector)
    {
        var disposalTask = DisposeCoreAsync(collector);
        await disposalTask.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (disposalTask.IsFaulted)
        {
            Volatile.Write(ref disposeFaulted, 1);
        }
    }

    private async Task DisposeCoreAsync(Task? collector)
    {
        var cancellation = CancelLifetimeAsync();
        await cancellation.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        Volatile.Write(ref cancelFaulted, cancellation.IsFaulted ? 1 : 0);
        if (collector is not null)
        {
            await collector.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
            Volatile.Write(ref captureFaulted, collector.IsFaulted ? 1 : 0);
        }

        lifetime.Dispose();
    }

    private async Task CancelLifetimeAsync()
    {
        var cancellation = lifetime.CancelAsync();
        await cancellation.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        if (cancellation.IsFaulted)
        {
            Volatile.Write(ref cancelFaulted, 1);
        }
    }

    private void Retain(ComparisonLifecycleRecord record)
    {
        lock (recordsGate)
        {
            records[nextRecord] = record;
            nextRecord = (nextRecord + 1) % records.Length;
            retainedRecords = Math.Min(retainedRecords + 1, records.Length);
        }
    }

    private string CreateFailureOutput()
    {
        var current = new ComparisonLifecycleRecord[ResourceNames.Length];
        var currentSequence = Interlocked.Read(ref observerSequence);
        for (var index = 0; index < ResourceNames.Length; index++)
        {
            var name = ResourceNames[index];
            current[index] = notifications.TryGetCurrentState(name, out var state)
                ? ComparisonLifecycleRecord.FromNative(name, state.Snapshot, currentSequence)
                : ComparisonLifecycleRecord.NotObserved(name, currentSequence);
        }

        ComparisonLifecycleRecord[] history;
        lock (recordsGate)
        {
            history = SnapshotRecords();
        }

        var observer = GetObserverCategory();
        return ComparisonLifecycleRecord.FormatFailureOutput(current, history, observer);
    }

    private ComparisonLifecycleRecord[] SnapshotRecords()
    {
        var snapshot = new ComparisonLifecycleRecord[retainedRecords];
        var first = (nextRecord - retainedRecords + records.Length) % records.Length;
        for (var index = 0; index < retainedRecords; index++)
        {
            snapshot[index] = records[(first + index) % records.Length]
                ?? throw new InvalidOperationException("Lifecycle ring contains an empty slot.");
        }

        return snapshot;
    }

    private string GetObserverCategory()
    {
        if (capture?.IsFaulted == true || Volatile.Read(ref captureFaulted) != 0)
        {
            return "Faulted";
        }

        if (Volatile.Read(ref outputFaulted) != 0 || Volatile.Read(ref disposeFaulted) != 0
            || Volatile.Read(ref cancelFaulted) != 0)
        {
            return "DiagnosticFaulted";
        }

        if (capture?.IsCanceled == true)
        {
            return "Cancelled";
        }

        return capture?.IsCompleted == true ? "Completed" : capture is null ? "NotStarted" : "Observing";
    }

    private int outputFaulted;
    private int disposeFaulted;
}
