using System.Diagnostics;
using Microsoft.Extensions.Options;
using System.Globalization;
using System.Text;

namespace KeyLoad.Comparisons;

internal sealed class ComparisonProgressObserver : IAsyncDisposable
{
    private const int NoObservedItems = 0;

    private const string LineFormat = "KeyLoadBenchmarkProgress phase={0} repetition={1} completed={2} total={3} failed={4} elapsedSeconds={5:F3}";
    private static readonly CompositeFormat ProgressFormat = CompositeFormat.Parse(LineFormat);
    private readonly Action<string>? _progress;
    private readonly System.Threading.Lock _outputGate = new();
    private readonly Stopwatch _elapsed = Stopwatch.StartNew();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly PeriodicTimer _timer;
    private readonly Task _heartbeat;
    private ComparisonProgressState _state = new(ComparisonProgressPhase.Oracle, NoObservedItems, NoObservedItems);
    private bool _hasState;

    internal ComparisonProgressObserver(Action<string>? progress, IOptions<NativeComparisonExecutionOptions> executionOptions)
    {
        var settings = executionOptions.Value;
        settings.Validate();
        _timer = new(settings.ProgressHeartbeatInterval);
        _progress = progress;
        _heartbeat = progress is null ? Task.CompletedTask : Task.Run(ObserveAsync);
    }

    internal void Begin(ComparisonProgressPhase phase, int repetition, int total = ComparisonProgressObserver.NoObservedItems, int completed = ComparisonProgressObserver.NoObservedItems, int failed = ComparisonProgressObserver.NoObservedItems)
    {
        const int NoObservedItems = 0;

        if (!Enum.IsDefined(phase) || repetition < NoObservedItems || total < NoObservedItems || completed < NoObservedItems || completed > total || failed < NoObservedItems || failed > completed)
        {
            throw new ArgumentOutOfRangeException(nameof(phase));
        }
        lock (_outputGate)
        {
            if (_hasState)
            {
                Emit();
            }
            Volatile.Write(ref _state, new(phase, repetition, total, completed, failed));
            _hasState = true;
            Emit();
        }
    }

    internal void Settle(bool success) => Volatile.Read(ref _state).Settle(success);

    internal void Complete()
    {
        var state = Volatile.Read(ref _state);
        Begin(ComparisonProgressPhase.Complete, state.Repetition, state.Total, state.Completed, state.Failed);
    }

    public async ValueTask DisposeAsync()
    {
        await _lifetime.CancelAsync();
        _timer.Dispose();
        try
        {
            await _heartbeat;
            lock (_outputGate)
            {
                Emit();
            }
        }
        finally
        {
            _lifetime.Dispose();
        }
    }

    private async Task ObserveAsync()
    {
        try
        {
            while (await _timer.WaitForNextTickAsync(_lifetime.Token))
            {
                lock (_outputGate)
                {
                    Emit();
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The owning runner stops and joins the observer before leaving its scope.
        }
    }

    private void Emit()
    {
        if (_progress is null)
        {
            return;
        }
        var state = Volatile.Read(ref _state);
        var completed = state.Completed;
        var failed = Math.Min(state.Failed, completed);
        var line = string.Format(CultureInfo.InvariantCulture, ProgressFormat, PhaseName(state.Phase), state.Repetition,
            completed, state.Total, failed, _elapsed.Elapsed.TotalSeconds);
        try
        {
            _progress(line);
        }
        catch (IOException)
        {
            // A failed diagnostic output must preserve the native workload outcome.
        }
        catch (UnauthorizedAccessException)
        {
            // Diagnostic output may lose file access while a workload is active.
        }
        catch (ObjectDisposedException)
        {
            // A closed diagnostic writer must not fail the workload or observer cleanup.
        }
    }

    private static string PhaseName(ComparisonProgressPhase phase) => phase switch
    {
        ComparisonProgressPhase.Oracle => Oracle,
        ComparisonProgressPhase.Initialize => Initialize,
        ComparisonProgressPhase.Warmup => Warmup,
        ComparisonProgressPhase.Prepare => Prepare,
        ComparisonProgressPhase.Measure => Measure,
        ComparisonProgressPhase.Validate => Validate,
        ComparisonProgressPhase.Complete => CompletePhase,
        _ => throw new ArgumentOutOfRangeException(nameof(phase))
    };

    private const string Oracle = "oracle";
    private const string Initialize = "initialize";
    private const string Warmup = "warmup";
    private const string Prepare = "prepare";
    private const string Measure = "measure";
    private const string Validate = "validate";
    private const string CompletePhase = "complete";
}
