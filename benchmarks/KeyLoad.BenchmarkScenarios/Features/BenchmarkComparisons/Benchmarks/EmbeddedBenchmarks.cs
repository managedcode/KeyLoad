using BenchmarkDotNet.Attributes;
using KeyLoad.Core;
using KeyLoad.Query;
using KeyLoad.Security;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Provides embedded scenarios for BenchmarkDotNet's generated external consumer.</summary>
[MemoryDiagnoser]
public class EmbeddedBenchmarks : IDisposable
{
    private const string TemporaryDirectoryPrefix = "keyload-benchmark-";
    private const string TenantId = "benchmark";
    private const string DatabaseId = "database";
    private const string CollectionId = "documents";
    private const string BenchmarkRecordId = "1";
    private const string PrincipalId = "root";
    private const string PrincipalScope = "system";
    private const string Wildcard = "*";
    private const string BenchmarkCredential = "root.benchmark-credential-32-characters";
    private const string DocumentJson = "{\"text\":\"clustered document database\",\"number\":42}";
    private const string TenantKeyPart = "tenant";
    private const string PartitionKeyPart = "partition";
    private const string DocumentKeyPart = "document";
    private const decimal CompositeKeyNumber = 42m;
    private const string ActiveSetupMessage = "The benchmark fixture already has an active setup.";

    private readonly PartitionRef partition = new(TenantId, DatabaseId, CollectionId, BenchmarkRecordId);
    private EmbeddedBenchmarkResources? resources;
    private bool disposed;

    /// <summary>Initializes a fresh embedded fixture for BenchmarkDotNet or a real caller.</summary>
    public EmbeddedBenchmarks()
    {
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

        var candidate = new EmbeddedBenchmarkResources();
        var initialized = false;
        try
        {
            candidate.Initialize(partition);
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
        => RequireResources().Database.GetDocument(PrincipalId, new(partition, CollectionId, BenchmarkRecordId));

    /// <summary>Encodes the existing five-component composite-key input.</summary>
    [Benchmark]
    public byte[] CompositeKey()
        => KeyLoad.Storage.KeyCodec.Encode(TenantKeyPart, DatabaseId, PartitionKeyPart, CompositeKeyNumber, DocumentKeyPart);

    /// <summary>Computes cosine similarity over the existing eight-dimensional input pair.</summary>
    [Benchmark]
    public double ExactCosine()
        => SearchEngine.Similarity([1, 2, 3, 4, 5, 6, 7, 8], [8, 7, 6, 5, 4, 3, 2, 1], DistanceMetric.Cosine);

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

    private EmbeddedBenchmarkResources RequireResources()
        => resources ?? throw new InvalidOperationException(EmbeddedBenchmarkResources.InactiveFixtureMessage);

    private sealed class EmbeddedBenchmarkResources : IDisposable
    {
        internal const string InactiveFixtureMessage = "The benchmark fixture is not initialized.";
        private const string GuidFormat = "N";
        private readonly string directory = Path.Combine(Path.GetTempPath(),
            TemporaryDirectoryPrefix + Guid.NewGuid().ToString(GuidFormat));
        private ZoneTreeStore? store;
        private DatabaseEngine? database;
        private bool disposed;

        internal DatabaseEngine Database
            => database ?? throw new InvalidOperationException(InactiveFixtureMessage);

        internal void Initialize(PartitionRef partition)
        {
            store = new(new(directory));
            database = new(store, new AuthorizationPolicy());
            database.Bootstrap(new(PrincipalId, PrincipalScope,
                [new(Wildcard, Wildcard, Capability.All)], [Wildcard])
            { ClusterAdministrator = true },
                DatabaseEngine.Credential(PrincipalId, PrincipalId, BenchmarkCredential));
            var shardId = store.Identity.NodeId;
            var catalog = new BootstrapPhysicalShardCatalogRequest(1, 0, shardId, store.Identity.Incarnation,
                [shardId.ToString(GuidFormat)]);
            Database.Apply(Database.CreateNativeOperation(OperationKind.BootstrapPhysicalShardCatalog,
                PhysicalShardCatalogIdentity.CreateBootstrapCommandId(shardId), PrincipalId,
                TimeProvider.System.GetUtcNow(), NativeSerialization.Serialize(catalog))).Get<bool>();
            Submit(OperationKind.ConfigureResource,
                new ConfigureResourceRequest(TenantId, DatabaseId,
                    new(CollectionId, ResourceKind.Collection, CollectionId)));
            Submit(OperationKind.Batch,
                new CommandRequest(Guid.NewGuid(), partition,
                    [new PutDocument(CollectionId, BenchmarkRecordId, DocumentJson)]));
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            try
            {
                if (disposing)
                {
                    store?.Dispose();
                }
            }
            finally
            {
                store = null;
                database = null;
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        private void Submit<T>(OperationKind kind, T payload)
        {
            var commandId = payload is CommandRequest command ? command.CommandId : Guid.NewGuid();
            var result = Database.Apply(new(commandId, kind, PrincipalId, TimeProvider.System.GetUtcNow(),
                System.Text.Json.JsonSerializer.Serialize(payload, JsonDefaults.Options)));
            if (result.Error is { } error)
            {
                throw Errors.Fail(error, result.SafeDetail!);
            }
        }
    }
}
