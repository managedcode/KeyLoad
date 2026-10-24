using System.Globalization;
using BenchmarkDotNet.Attributes;

namespace KeyLoad.BenchmarkScenarios.BenchmarkComparisons;

/// <summary>Measures durable single-document commits from concurrent writers against the real ZoneTree store.</summary>
/// <remarks>A local development measurement: each invocation commits a fixed number of new documents split across
/// dedicated writer threads, so the per-operation time shows how commits from parallel callers share the journal.</remarks>
[MemoryDiagnoser]
public class CommitThroughputBenchmarks : IDisposable
{
    private const int CommitsPerInvocation = 512;
    private const string CollectionId = "commits";
    private const string PartitionKey = "writers";
    private const string DocumentJson = "{\"text\":\"durable commit\",\"number\":42}";
    private const string ActiveSetupMessage = "The commit benchmark fixture already has an active setup.";

    private readonly PartitionRef partition = new(EmbeddedBenchmarkDatabase.TenantId, EmbeddedBenchmarkDatabase.DatabaseId,
        CollectionId, PartitionKey);
    private EmbeddedBenchmarkDatabase? resources;
    private long nextDocument;
    private bool disposed;

    /// <summary>Gets or sets the number of concurrent writer threads, or of commands sharing one grouped commit.</summary>
    [Params(1, 16)]
    public int Writers { get; set; }

    /// <summary>Creates the real ZoneTree-backed database and its collection.</summary>
    [GlobalSetup]
    public void Setup()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (resources is not null)
        {
            throw new InvalidOperationException(ActiveSetupMessage);
        }

        var candidate = new EmbeddedBenchmarkDatabase(EmbeddedBenchmarkRuntimeRegistration.Read(), TimeProvider.System);
        try
        {
            candidate.Initialize(CollectionId);
            resources = candidate;
        }
        finally
        {
            if (resources is null)
            {
                candidate.Dispose();
            }
        }
    }

    /// <summary>Commits new documents from the configured number of writers until the invocation's share is done.</summary>
    [Benchmark(OperationsPerInvoke = CommitsPerInvocation)]
    public void ConcurrentDocumentCommits()
    {
        var owner = resources ?? throw new InvalidOperationException(EmbeddedBenchmarkDatabase.InactiveFixtureMessage);
        var first = Interlocked.Add(ref nextDocument, CommitsPerInvocation) - CommitsPerInvocation;
        var remaining = CommitsPerInvocation;
        var threads = new Thread[Writers];
        for (var index = 0; index < threads.Length; index++)
        {
            threads[index] = new Thread(() =>
            {
                while (Interlocked.Decrement(ref remaining) >= 0)
                {
                    var id = Interlocked.Increment(ref first).ToString(CultureInfo.InvariantCulture);
                    owner.Commit(partition, new PutDocument(CollectionId, id, DocumentJson));
                }
            });
            threads[index].Start();
        }
        foreach (var thread in threads)
        {
            thread.Join();
        }
    }

    /// <summary>Commits new documents in consecutive groups of <see cref="Writers"/> commands per storage commit.</summary>
    /// <remarks>Each group shares one atomic commit and one journal flush, as concurrent writers do through the
    /// database grain; the per-operation time is the cost of one acknowledged command.</remarks>
    [Benchmark(OperationsPerInvoke = CommitsPerInvocation)]
    public void GroupedDocumentCommits()
    {
        var owner = resources ?? throw new InvalidOperationException(EmbeddedBenchmarkDatabase.InactiveFixtureMessage);
        var first = Interlocked.Add(ref nextDocument, CommitsPerInvocation) - CommitsPerInvocation;
        for (var group = 0; group < CommitsPerInvocation; group += Writers)
        {
            var documents = new Mutation[Math.Min(Writers, CommitsPerInvocation - group)];
            for (var index = 0; index < documents.Length; index++)
            {
                var id = (first + group + index).ToString(CultureInfo.InvariantCulture);
                documents[index] = new PutDocument(CollectionId, id, DocumentJson);
            }
            owner.CommitGroup(partition, documents);
        }
    }

    /// <summary>Releases the fixture's database, store and directory.</summary>
    [GlobalCleanup]
    public void Cleanup() => Dispose();

    /// <summary>Releases fixture resources; repeated disposal is harmless.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the owned resources when disposal is requested.</summary>
    protected virtual void Dispose(bool disposing)
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        var owned = resources;
        resources = null;
        if (disposing)
        {
            owned?.Dispose();
        }
    }
}
