using System.Diagnostics;
using Microsoft.Extensions.Options;
using System.Runtime.ExceptionServices;

namespace KeyLoad.BenchmarkScenarios.Features.BenchmarkComparisons;

internal sealed class ScaledRawStorageFixture : IDisposable
{
    private readonly CancellationTokenSource _lifetime;
    private ScaledRawStorageFixtureCore? _core;
    private bool _closing;
    private bool _disposed;
    private const string InactiveMessage = "The scaled native fixture is not initialized.";
    private const string DeadlineMessage = "The scaled fixture preparation deadline expired.";

    internal ScaledRawStorageFixture(int recordCount, int payloadBytes, IOptions<ScaledStorageExecutionOptions> executionOptions,
        CancellationToken cancellationToken = default)
    {
        var settings = executionOptions.Value;
        settings.Validate();
        ScaledRawStorageSettings.ValidateInput(recordCount, payloadBytes);
        cancellationToken.ThrowIfCancellationRequested();
        var deadlineStart = Stopwatch.GetTimestamp();
        var processMemoryCeiling = ScaledRawStorageSettings.ValidateFixtureCapacity(recordCount, payloadBytes, settings);
        var remaining = settings.PreparationTimeout - Stopwatch.GetElapsedTime(deadlineStart);
        if (remaining <= TimeSpan.Zero)
        {
            throw new TimeoutException(DeadlineMessage);
        }

        _lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            _lifetime.CancelAfter(remaining);
            _lifetime.Token.ThrowIfCancellationRequested();
        }
        catch (Exception failure) when (RawStorageFixtureFailures.IsNonFatal(failure))
        {
            _lifetime.Dispose();
            throw;
        }

        try
        {
            ScaledRawStorageOwnership.Admit(this);
        }
        catch (Exception failure) when (RawStorageFixtureFailures.IsNonFatal(failure))
        {
            _lifetime.Dispose();
            throw;
        }

        try
        {
            _core = new ScaledRawStorageFixtureCore(recordCount, payloadBytes,
                deadlineStart, processMemoryCeiling, _lifetime.Token, executionOptions);
            _core.Initialize();
        }
        catch (Exception primary) when (RawStorageFixtureFailures.IsNonFatal(primary))
        {
            HandleInitializationFailure(primary);
        }
    }

    internal static bool HasRetainedFailedOwner => ScaledRawStorageOwnership.HasRetainedFailedOwner;

    internal static bool RetryFailedOwnerClose() => ScaledRawStorageOwnership.RetryFailedOwnerClose();

    internal int RecordCount => RequireCore().RecordCount;
    internal int PayloadBytes => RequireCore().PayloadBytes;
    internal ScaledRawStorageCorpus Corpus => RequireCore().Corpus;
    internal ScaledRawStorageReadOrder ReadOrder => RequireCore().ReadOrder;
    internal string? Directory => _core?.Directory;

    internal bool TryRead(int index, out ReadOnlyMemory<byte> value)
        => RequireCore().TryRead(index, out value);

    internal ulong Read(int index) => RequireCore().Read(index);
    internal ulong ReadNextSequential() => RequireCore().ReadNextSequential();
    internal ulong ReadNextRandom() => RequireCore().ReadNextRandom();
    internal void VerifyAll() => RequireCore().VerifyAll();
    internal ScaledRawStorageSnapshot Capture() => RequireCore().Capture();

    public void Dispose()
    {
        try
        {
            if (!_disposed)
            {
                ScaledRawStorageOwnership.CloseHealthy(this);
            }
        }
        finally
        {
            if (_disposed)
            {
                GC.SuppressFinalize(this);
            }
        }
    }

    internal bool IsClosed => _disposed;

    internal void MarkClosing()
    {
        _closing = true;
        _core?.MarkClosing();
    }

    internal void CloseActiveResources() => CloseResources(performPostOracle: true);

    internal void CloseFailedConstructionResources() => CloseResources(performPostOracle: false);

    private void HandleInitializationFailure(Exception primary)
    {
        if (_core is null)
        {
            _lifetime.Dispose();
            _disposed = true;
            ScaledRawStorageOwnership.ReleaseUninitialized(this);
            ExceptionDispatchInfo.Capture(primary).Throw();
            return;
        }

        Exception? closeFailure = null;
        try
        {
            ScaledRawStorageOwnership.CloseFailedConstruction(this);
        }
        catch (Exception failure) when (RawStorageFixtureFailures.IsNonFatal(failure))
        {
            closeFailure = failure;
        }

        if (closeFailure is not null)
        {
            throw new AggregateException(primary, closeFailure);
        }

        ExceptionDispatchInfo.Capture(primary).Throw();
    }

    private void CloseResources(bool performPostOracle)
    {
        MarkClosing();
        Exception? primary = null;
        var core = _core;
        if (core is not null)
        {
            try
            {
                core.Close(performPostOracle);
            }
            catch (Exception failure) when (RawStorageFixtureFailures.IsNonFatal(failure))
            {
                primary = failure;
            }

            if (core.IsDisposed)
            {
                _core = null;
            }
        }

        if (_core is null)
        {
            _lifetime.Dispose();
            _disposed = true;
        }

        if (primary is not null)
        {
            ExceptionDispatchInfo.Capture(primary).Throw();
        }
    }

    private ScaledRawStorageFixtureCore RequireCore()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ObjectDisposedException.ThrowIf(_closing, this);
        return _core ?? throw new InvalidOperationException(InactiveMessage);
    }
}
