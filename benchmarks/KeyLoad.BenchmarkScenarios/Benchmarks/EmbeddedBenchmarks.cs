using BenchmarkDotNet.Attributes;
using KeyLoad.Core;
using KeyLoad.Query;

namespace KeyLoad.BenchmarkScenarios.BenchmarkComparisons;

/// <summary>Provides embedded scenarios for BenchmarkDotNet's generated external consumer.</summary>
[MemoryDiagnoser]
public class EmbeddedBenchmarks : IDisposable
{
    private const float EighthVectorCoordinate = 8;
    private const float SeventhVectorCoordinate = 7;
    private const float SixthVectorCoordinate = 6;
    private const float FifthVectorCoordinate = 5;
    private const float FourthVectorCoordinate = 4;
    private const int SecondVectorCoordinate = 2;
    private const int ThirdVectorCoordinate = 3;

    private const string DatabaseId = "database";
    private const string CollectionId = "documents";
    private const string BenchmarkRecordId = "1";
    private const string DocumentJson = "{\"text\":\"clustered document database\",\"number\":42}";
    private const string TenantKeyPart = "tenant";
    private const string PartitionKeyPart = "partition";
    private const string DocumentKeyPart = "document";
    private const decimal CompositeKeyNumber = 42m;
    private const string ActiveSetupMessage = "The benchmark fixture already has an active setup.";

    private readonly TimeProvider timeProvider;
    private readonly PartitionRef partition = new(EmbeddedBenchmarkDatabase.TenantId, EmbeddedBenchmarkDatabase.DatabaseId,
        CollectionId, BenchmarkRecordId);
    private EmbeddedBenchmarkDatabase? resources;
    private bool disposed;

    /// <summary>Initializes a fresh embedded fixture for BenchmarkDotNet or a real caller.</summary>
    public EmbeddedBenchmarks() : this(TimeProvider.System)
    {
    }

    /// <summary>Creates a fixture borrowing the supplied command clock.</summary>
    public EmbeddedBenchmarks(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        this.timeProvider = timeProvider;
    }

    /// <summary>Creates the real ZoneTree-backed database and seeds its benchmark document.</summary>
    [GlobalSetup]
    public void Setup()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (resources is not null)
        {
            throw new InvalidOperationException(ActiveSetupMessage);
        }

        var candidate = new EmbeddedBenchmarkDatabase(EmbeddedBenchmarkRuntimeRegistration.Read(), timeProvider);
        var initialized = false;
        try
        {
            candidate.Initialize(CollectionId);
            candidate.Commit(partition, new PutDocument(CollectionId, BenchmarkRecordId, DocumentJson));
            resources = candidate;
            initialized = true;
        }
        finally
        {
            if (!initialized)
            {
                candidate.Dispose();
            }
        }
    }

    /// <summary>Reads the exact document seeded during global setup.</summary>
    [Benchmark]
    public DocumentResult? PointRead()
        => RequireResources().Database.GetDocument(EmbeddedBenchmarkDatabase.PrincipalId, new(partition, CollectionId, BenchmarkRecordId));

    /// <summary>Encodes the existing five-component composite-key input.</summary>
    [Benchmark]
    public byte[] CompositeKey()
        => KeyLoad.Storage.KeyCodec.Encode(TenantKeyPart, DatabaseId, PartitionKeyPart, CompositeKeyNumber, DocumentKeyPart);

    /// <summary>Computes cosine similarity over the existing eight-dimensional input pair.</summary>
    [Benchmark]
    public double ExactCosine()
        => SearchEngine.Similarity([1, SecondVectorCoordinate, ThirdVectorCoordinate, FourthVectorCoordinate, FifthVectorCoordinate, SixthVectorCoordinate, SeventhVectorCoordinate, EighthVectorCoordinate], [EighthVectorCoordinate, SeventhVectorCoordinate, SixthVectorCoordinate, FifthVectorCoordinate, FourthVectorCoordinate, ThirdVectorCoordinate, SecondVectorCoordinate, 1], DistanceMetric.Cosine);

    /// <summary>Releases the fixture's database, store and temporary directory.</summary>
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
        var ownedResources = resources;
        resources = null;
        if (disposing)
        {
            ownedResources?.Dispose();
        }
    }

    private EmbeddedBenchmarkDatabase RequireResources()
        => resources ?? throw new InvalidOperationException(EmbeddedBenchmarkDatabase.InactiveFixtureMessage);
}
