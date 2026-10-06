using System.Diagnostics;
using Microsoft.Extensions.Options;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal sealed class ScaledRawStorageFixtureCore
{
    private const string InitializedMessage = "The scaled fixture core initialization is one-shot.";
    private readonly ScaledStorageExecutionOptions settings;
    private readonly IOptions<ScaledStorageExecutionOptions> executionOptions;
    private readonly CancellationToken _token;
    private readonly long _deadlineStart;
    private readonly long _processMemoryCeilingBytes;
    private readonly byte[] _seedScratch;
    private readonly byte[] _readScratch;
    private readonly byte[] _expectedScratch;
    private readonly ScaledRawStorageCorpus _corpus;
    private readonly ScaledRawStorageReadOrder _readOrder;
    private readonly ScaledRawStorageValueArena _arena;
    private ScaledRawStorageZoneTreeEngine? _engine;
    private long _seedAttempts;
    private long _successfulSeedWrites;
    private long _verifiedRecords;
    private long _verificationPasses;
    private long _nativeReadCalls;
    private long _seedElapsedTicks;
    private long _verificationElapsedTicks;
    private string _fullValueDigest = string.Empty;
    private bool _initializeStarted;
    private bool _initialized;
    private bool _postOracleAttempted;
    private bool _closing;
    private bool _disposed;

    internal ScaledRawStorageFixtureCore(int recordCount, int payloadBytes,
        long deadlineStart, long processMemoryCeilingBytes, IOptions<ScaledStorageExecutionOptions> executionOptions, CancellationToken token)
    {
        settings = executionOptions.Value;
        this.executionOptions = executionOptions;
        _token = token;
        _deadlineStart = deadlineStart;
        _processMemoryCeilingBytes = processMemoryCeilingBytes;
        RecordCount = recordCount;
        PayloadBytes = payloadBytes;
        _seedScratch = GC.AllocateUninitializedArray<byte>(payloadBytes, pinned: true);
        _readScratch = GC.AllocateUninitializedArray<byte>(payloadBytes, pinned: true);
        _expectedScratch = GC.AllocateUninitializedArray<byte>(payloadBytes);
        _corpus = new ScaledRawStorageCorpus(recordCount, payloadBytes, executionOptions, token);
        _readOrder = new ScaledRawStorageReadOrder(recordCount, executionOptions, token);
        _arena = new ScaledRawStorageValueArena(_corpus, executionOptions, token);
    }

    internal int RecordCount { get; }
    internal int PayloadBytes { get; }
    internal bool IsDisposed => _disposed;
    internal ScaledRawStorageCorpus Corpus => _corpus;
    internal ScaledRawStorageReadOrder ReadOrder => _readOrder;
    internal string? Directory => _engine?.Directory;

    internal void MarkClosing() => _closing = true;

    internal void Initialize()
    {
        ObjectDisposedException.ThrowIf(_closing || _disposed, this);
        if (_initializeStarted)
        {
            throw new InvalidOperationException(InitializedMessage);
        }

        _initializeStarted = true;
        ScaledRawStoragePreparationGuard.Check(_deadlineStart, executionOptions, _token);
        var engine = new ScaledRawStorageZoneTreeEngine(_corpus, _arena, _readScratch, _deadlineStart, executionOptions, _token);
        _engine = engine;
        engine.Initialize();
        _seedElapsedTicks = ScaledRawStorageSeedRunner.Run(_corpus, engine, _seedScratch,
            RecordCount, _deadlineStart, ref _seedAttempts, ref _successfulSeedWrites, executionOptions, _token);
        VerifyAllCore(enforceBudget: true);
        _initialized = true;
    }

    internal bool TryRead(int index, out ReadOnlyMemory<byte> value)
    {
        EnsureOpen();
        if ((uint)index > (uint)RecordCount)
        {
            throw new ArgumentOutOfRangeException(nameof(index));
        }

        _nativeReadCalls++;
        return _engine!.TryRead(index, out value);
    }

    internal ulong Read(int index) => ReadOne(index);

    internal ulong ReadNextSequential()
    {
        EnsureOpen();
        return ReadOne(_readOrder.NextSequential());
    }

    internal ulong ReadNextRandom()
    {
        EnsureOpen();
        return ReadOne(_readOrder.NextRandom());
    }

    internal void VerifyAll()
    {
        EnsureOpen();
        VerifyAllCore(enforceBudget: true);
    }

    internal ScaledRawStorageSnapshot Capture()
    {
        EnsureOpen();
        var native = _engine!.Capture();
        using var process = Process.GetCurrentProcess();
        var peak = ScaledRawStorageProcessMemory.ReadPeakBytes(process);
        var available = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
        ScaledRawStorageMemoryGuard.ValidateObserved(peak, available, _processMemoryCeilingBytes, settings.RequiredHeadroomBytes);
        return ScaledRawStorageSnapshotFactory.Create(this, native, peak, process.WorkingSet64, available);
    }

    internal void Close(bool performPostOracle)
    {
        if (_disposed)
        {
            return;
        }

        _closing = true;
        Exception? primary = null;
        var failures = new List<Exception>();
        if (performPostOracle && _initialized && !_postOracleAttempted)
        {
            _postOracleAttempted = true;
            try
            {
                VerifyAllCore(enforceBudget: false);
            }
            catch (Exception failure) when (RawStorageFixtureFailures.IsNonFatal(failure))
            {
                primary = failure;
            }
        }

        if (_engine is not null && RawStorageFixtureFailures.Capture(_engine.Dispose, failures))
        {
            _engine = null;
        }

        _disposed = _engine is null;
        RawStorageFixtureFailures.Throw(primary, failures);
    }

    internal long NativeReadCalls => _nativeReadCalls;
    internal long ProcessMemoryCeilingBytes => _processMemoryCeilingBytes;
    internal long SeedAttempts => _seedAttempts;
    internal long SuccessfulSeedWrites => _successfulSeedWrites;
    internal long VerifiedRecords => _verifiedRecords;
    internal long VerificationPasses => _verificationPasses;
    internal long SeedElapsedTicks => _seedElapsedTicks;
    internal long VerificationElapsedTicks => _verificationElapsedTicks;
    internal string FullValueDigest => _fullValueDigest;
    internal long RetainedKeyBytes => _corpus.RetainedKeyBytes;
    internal long RetainedValueBytes => _arena.RetainedValueBytes;
    internal long RetainedOrderBytes => _readOrder.RetainedOrderBytes;
    internal string PermutationDigest => _readOrder.PermutationDigest;

    private ulong ReadOne(int index)
    {
        EnsureOpen();
        return ScaledRawStorageIdentityReader.Read(_corpus, _engine!, index, ref _nativeReadCalls);
    }

    private void EnsureOpen()
        => ObjectDisposedException.ThrowIf(_closing || _disposed, this);

    private void VerifyAllCore(bool enforceBudget)
    {
        const long NoPreparationDeadline = 0L;
        var result = ScaledRawStorageVerification.Run(_corpus, _engine!, _expectedScratch,
            RecordCount, ref _nativeReadCalls, enforceBudget ? _deadlineStart : NoPreparationDeadline,
            enforceBudget, executionOptions, enforceBudget ? _token : CancellationToken.None);
        _ = _engine!.Capture();
        if (enforceBudget)
        {
            ScaledRawStoragePreparationGuard.Check(_deadlineStart, executionOptions, _token);
        }

        using var process = Process.GetCurrentProcess();
        ScaledRawStorageMemoryGuard.ValidateObserved(ScaledRawStorageProcessMemory.ReadPeakBytes(process),
            GC.GetGCMemoryInfo().TotalAvailableMemoryBytes, _processMemoryCeilingBytes, settings.RequiredHeadroomBytes);
        _verifiedRecords = result.VerifiedRecords;
        _verificationPasses++;
        _fullValueDigest = result.Digest;
        _verificationElapsedTicks = result.ElapsedTicks;
    }
}
