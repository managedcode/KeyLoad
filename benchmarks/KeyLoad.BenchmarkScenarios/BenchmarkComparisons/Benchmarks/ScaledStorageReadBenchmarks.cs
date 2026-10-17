using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using Microsoft.Extensions.Options;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

/// <summary>Measures checked sequential and deterministic shuffled ZoneTree reads at the selected scale.</summary>
[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, launchCount: LaunchCount, warmupCount: WarmupCount,
    iterationCount: IterationCount, invocationCount: InvocationCount)]
[Config(typeof(ScaledStorageReadUnrollConfiguration))]
public class ScaledStorageReadBenchmarks : IDisposable
{
    private const string ZoneTreeLabel = "zonetree";
    private const int SmallPayloadBytes = 32;
    private const int LargePayloadBytes = 1024;
    private const int HundredThousand = 100_000;
    private const int OneMillion = 1_000_000;
    private const int ReadsPerInvocation = 1_000_000;
    private const int LaunchCount = 1;
    private const int WarmupCount = 8;
    private const int IterationCount = 10;
    private const int InvocationCount = 1;
    private const string InvalidEngineMessage = "The scaled benchmark engine label is unsupported.";
    private const string InvalidCountMessage = "The scaled benchmark record-count selection is unsupported.";
    private const string InactiveMessage = "The scaled benchmark fixture is not initialized.";
    private const string IdentityMessage = "A scaled read returned an unexpected record identity.";
    private const string ActiveMessage = "The scaled benchmark already owns a fixture.";
    private const string ChecksumMessage = "A scaled benchmark checksum differs from the exact full-index oracle.";
    private ScaledRawStorageFixture? _fixture;
    private bool _disposed;
    private IOptions<BenchmarkScenarioSelectionOptions>? selection;

    /// <summary>Gets or sets the one exact lowercase ZoneTree selection.</summary>
    [ParamsSource(nameof(Engines))]
    public string Engine { get; set; } = ZoneTreeLabel;

    /// <summary>Gets or sets the value payload length.</summary>
    [Params(SmallPayloadBytes, LargePayloadBytes)]
    public int PayloadBytes { get; set; } = SmallPayloadBytes;

    /// <summary>Gets or sets the exact qualification record count.</summary>
    [ParamsSource(nameof(RecordCounts))]
    public int RecordCount { get; set; } = HundredThousand;

    /// <summary>Provides exactly one process-selected engine.</summary>
    public IEnumerable<string> Engines => [Selection.Value.Engine];

    /// <summary>Provides exactly one process-selected qualification count.</summary>
    public IEnumerable<int> RecordCounts => [BenchmarkScenarioSelectionRegistration.RecordCount(Selection.Value)];

    /// <summary>Creates the real bounded engine and completes its full setup oracle.</summary>
    [GlobalSetup]
    public void Setup()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_fixture is not null)
        {
            throw new InvalidOperationException(ActiveMessage);
        }

        ValidateEngine(Engine);
        if (RecordCount is not (HundredThousand or OneMillion))
        {
            throw new ArgumentOutOfRangeException(nameof(RecordCount), InvalidCountMessage);
        }

        _fixture = new ScaledRawStorageFixture(RecordCount, PayloadBytes, ScaledStorageExecutionRegistration.Read());
    }

    /// <summary>Consumes one million checked sequential actual record identities.</summary>
    [Benchmark(OperationsPerInvoke = ReadsPerInvocation)]
    public ulong SequentialRead()
    {
        const int ChecksumInitialValue = 0;
        const int OperationInitialValue = 0;

        var fixture = RequireFixture();
        ulong checksum = ChecksumInitialValue;
        for (var operation = OperationInitialValue; operation < ReadsPerInvocation; operation++)
        {
            var identity = fixture.ReadNextSequential();
            if (identity != (ulong)(operation % RecordCount))
            {
                throw new InvalidOperationException(IdentityMessage);
            }

            checksum += identity;
        }

        return ValidateChecksum(checksum);
    }

    /// <summary>Consumes one million checked shuffled actual record identities.</summary>
    [Benchmark(OperationsPerInvoke = ReadsPerInvocation)]
    public ulong RandomRead()
    {
        const int ChecksumInitialValue = 0;
        const int OperationInitialValue = 0;

        var fixture = RequireFixture();
        ulong checksum = ChecksumInitialValue;
        for (var operation = OperationInitialValue; operation < ReadsPerInvocation; operation++)
        {
            var identity = fixture.ReadNextRandom();
            checksum += identity;
        }

        return ValidateChecksum(checksum);
    }

    /// <summary>Returns the active fixture's immutable cold snapshot to the friend test assembly.</summary>
    internal ScaledRawStorageSnapshot Capture() => RequireFixture().Capture();

    /// <summary>Performs a full native verification and closes every retained owner.</summary>
    [GlobalCleanup]
    public void Cleanup() => Dispose();

    /// <summary>Closes the fixture; failed close retains the owner for a later retry.</summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>Closes the owned fixture during managed disposal.</summary>
    /// <param name="disposing">True when called from <see cref="Dispose()"/>.</param>
    protected virtual void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }

        if (_disposed && _fixture is null)
        {
            return;
        }

        _fixture?.Dispose();
        _fixture = null;
        _disposed = true;
    }

    private IOptions<BenchmarkScenarioSelectionOptions> Selection
        => selection ??= BenchmarkScenarioSelectionRegistration.ReadScaled();

    private static void ValidateEngine(string label)
    {
        if (label != ZoneTreeLabel)
        {
            throw new ArgumentException(InvalidEngineMessage, nameof(label));
        }
    }

    private ScaledRawStorageFixture RequireFixture()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _fixture ?? throw new InvalidOperationException(InactiveMessage);
    }

    private ulong ValidateChecksum(ulong actual)
    {
        const int RecordCountStep = 1;
        const ulong ArithmeticSeriesDivisor = 2UL;

        var cycles = (ulong)(ReadsPerInvocation / RecordCount);
        var expected = checked(cycles * (ulong)RecordCount * (ulong)(RecordCount - RecordCountStep) / ArithmeticSeriesDivisor);
        if (actual != expected)
        {
            throw new InvalidOperationException(ChecksumMessage);
        }

        return actual;
    }
}

internal sealed class ScaledStorageReadUnrollConfiguration : ManualConfig
{
    private const int UnrollFactor = 1;

    public ScaledStorageReadUnrollConfiguration()
        => AddJob(Job.Default.WithUnrollFactor(UnrollFactor).AsMutator());
}
