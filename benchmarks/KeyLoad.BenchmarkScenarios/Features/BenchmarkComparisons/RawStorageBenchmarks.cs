using System.Runtime.ExceptionServices;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Measures the four frozen resident raw-byte operations on one selected native engine.</summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, launchCount: 1, warmupCount: 3, iterationCount: 5, invocationCount: 1024)]
[Config(typeof(RawStorageUnrollConfiguration))]
public class RawStorageBenchmarks : IDisposable
{
    private const string EngineEnvironmentVariable = "KEYLOAD_RAW_STORAGE_ENGINE";
    private const string ZoneTreeLabel = "zonetree";
    private const string TsavoriteLabel = "tsavorite";
    private const int BenchmarkRecordCount = 4096;
    private const int SmallPayloadBytes = 32;
    private const int LargePayloadBytes = 1024;
    private const int MaximumWrites = 65_536;
    private const int CreateDeleteOperationsPerInvoke = 2;
    private const string CreateDeletePairDescription = "one transient-key create/delete pair";
    private const string ActiveSetupMessage = "The raw storage benchmark already has an active fixture.";
    private const string InactiveSetupMessage = "The raw storage benchmark fixture is not initialized.";
    private const string SetupReadFailureMessage = "The raw engine setup oracle did not read the seeded value.";
    private const string SetupValueFailureMessage = "The raw engine setup oracle returned different bytes.";
    private const string SetupMissingFailureMessage = "The raw engine setup oracle found its reserved missing key.";
    private const string TimedMissingReadFailureMessage = "The reserved missing key unexpectedly became present.";
    private const string TimedCreateDeleteFailureMessage = "The transient key was not deleted after its successful upsert.";
    private const string UnsupportedEngineMessage = "The raw-storage engine label is unsupported.";

    private RawStorageFixture? fixture;
    private bool disposed;
    private bool nextOverwriteAlternate = true;

    /// <summary>Gets or sets the one lowercase engine label selected for the generated runner.</summary>
    [ParamsSource(nameof(Engines))]
    public string Engine { get; set; } = ZoneTreeLabel;

    /// <summary>Gets or sets the exact preallocated value length.</summary>
    [Params(SmallPayloadBytes, LargePayloadBytes)]
    public int PayloadBytes { get; set; } = SmallPayloadBytes;

    /// <summary>Gets or sets the number of seeded records in each benchmark fixture.</summary>
    [Params(BenchmarkRecordCount)]
    public int RecordCount { get; set; } = BenchmarkRecordCount;

    /// <summary>Returns exactly one engine selected by the process environment.</summary>
    public IEnumerable<string> Engines => [SelectEngine()];

    /// <summary>Creates and verifies the actual engine fixture before measurement begins.</summary>
    [GlobalSetup]
    public void Setup()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        if (fixture is not null)
        {
            throw new InvalidOperationException(ActiveSetupMessage);
        }

        var candidate = new RawStorageFixture(ParseEngine(Engine), RecordCount, PayloadBytes, MaximumWrites);
        try
        {
            VerifySetupOracle(candidate);
            fixture = candidate;
        }
        catch (Exception primary)
        {
            ThrowAfterCandidateCleanup(candidate, primary);
            throw;
        }
    }

    /// <summary>Returns the actual first byte from the hot seeded record zero.</summary>
    [Benchmark]
    public byte PointRead()
    {
        var active = RequireFixture();
        if (!active.TryRead(0, out var value))
        {
            throw new InvalidOperationException(SetupReadFailureMessage);
        }

        return value.Span[0];
    }

    /// <summary>Returns the actual found flag for the reserved unseeded missing key.</summary>
    [Benchmark]
    public bool MissingRead()
    {
        var found = RequireFixture().TryRead(RecordCount, out _);
        if (found)
        {
            throw new InvalidOperationException(TimedMissingReadFailureMessage);
        }

        return found;
    }

    /// <summary>Alternates the two retained immutable values on seeded record zero.</summary>
    [Benchmark]
    public void Overwrite()
    {
        var alternate = nextOverwriteAlternate;
        nextOverwriteAlternate = !nextOverwriteAlternate;
        RequireFixture().Upsert(0, alternate);
    }

    /// <summary>Creates and removes the reserved transient key as one explicitly labeled pair.</summary>
    [Benchmark(OperationsPerInvoke = CreateDeleteOperationsPerInvoke, Description = CreateDeletePairDescription)]
    public bool CreateDelete()
    {
        var active = RequireFixture();
        var transientIndex = RecordCount + 1;
        active.Upsert(transientIndex, alternate: true);
        var deleted = active.Delete(transientIndex);
        if (!deleted)
        {
            throw new InvalidOperationException(TimedCreateDeleteFailureMessage);
        }

        return deleted;
    }

    /// <summary>Releases the actual fixture; repeated cleanup is harmless.</summary>
    [GlobalCleanup]
    public void Cleanup() => Dispose();

    /// <summary>Closes the engine fixture after the generated runner settles its calls.</summary>
    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Releases the fixture on explicit disposal and preserves a failed owner for a close retry.</summary>
    /// <param name="disposing">True when managed owners may be released.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (disposed && fixture is null)
        {
            return;
        }

        disposed = true;
        if (!disposing || fixture is null)
        {
            return;
        }

        fixture.Dispose();
        fixture = null;
    }

    private static string SelectEngine()
    {
        var selected = Environment.GetEnvironmentVariable(EngineEnvironmentVariable) ?? ZoneTreeLabel;
        _ = ParseEngine(selected);
        return selected;
    }

    private static RawStorageEngineKind ParseEngine(string label)
        => label switch
        {
            ZoneTreeLabel => RawStorageEngineKind.ZoneTree,
            TsavoriteLabel => RawStorageEngineKind.Tsavorite,
            _ => throw new ArgumentException(UnsupportedEngineMessage, nameof(label))
        };

    private static void VerifySetupOracle(RawStorageFixture candidate)
    {
        if (!candidate.TryRead(0, out var actual))
        {
            throw new InvalidOperationException(SetupReadFailureMessage);
        }

        if (!actual.Span.SequenceEqual(candidate.Corpus.Value(0).Span))
        {
            throw new InvalidOperationException(SetupValueFailureMessage);
        }

        if (candidate.TryRead(candidate.Corpus.RecordCount, out _))
        {
            throw new InvalidOperationException(SetupMissingFailureMessage);
        }
    }

    private static void ThrowAfterCandidateCleanup(RawStorageFixture candidate, Exception primary)
    {
        try
        {
            candidate.Dispose();
        }
        catch (Exception cleanup)
        {
            throw new AggregateException(primary, cleanup);
        }

        ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private RawStorageFixture RequireFixture()
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        return fixture ?? throw new InvalidOperationException(InactiveSetupMessage);
    }
}

internal sealed class RawStorageUnrollConfiguration : ManualConfig
{
    /// <summary>Keeps the frozen default loop unroll without overriding the requested invocation count.</summary>
    public RawStorageUnrollConfiguration()
        => AddJob(Job.Default.WithUnrollFactor(1).AsMutator());
}
