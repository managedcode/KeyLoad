using System.Text;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Captures a bounded output prefix while draining the real redirected stream.</summary>
internal sealed class ComparisonHostOutputCapture
{
    internal const int MaximumCapturedCharacters = 32_768;
    internal const int CaptureChunkCharacters = 4_096;

    private const string MissingKeyLoadMarker = "Missing benchmark setting: Benchmarks:KeyLoadEndpoint";
    private const string MissingQdrantMarker = "Missing benchmark setting: Benchmarks:QdrantEndpoint";
    private const string InvalidDimensionsMarker = "The benchmark configuration exceeds its budgets.";

    private readonly System.Threading.Lock _sync = new();
    private readonly StringBuilder _text = new();
    private ComparisonHostCaptureState _state = ComparisonHostCaptureState.Unavailable;

    internal async Task<string> CaptureAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(reader);
        Start();
        try
        {
            var text = await ReadAndDrainAsync(reader, cancellationToken);
            SetState(ComparisonHostCaptureState.Completed);
            return text;
        }
        catch (OperationCanceledException)
        {
            SetState(ComparisonHostCaptureState.Canceled);
            throw;
        }
        finally
        {
            MarkFaultedIfRunning();
        }
    }

    private async Task<string> ReadAndDrainAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[CaptureChunkCharacters];
        int count;
        while ((count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken)) > 0)
        {
            Append(buffer, count);
        }

        lock (_sync)
        {
            return _text.ToString();
        }
    }

    private void Start()
    {
        lock (_sync)
        {
            if (_state != ComparisonHostCaptureState.Unavailable)
            {
                throw new InvalidOperationException("Output capture has already started.");
            }
            _state = ComparisonHostCaptureState.Running;
        }
    }

    private void Append(char[] buffer, int count)
    {
        lock (_sync)
        {
            var remaining = MaximumCapturedCharacters - _text.Length;
            if (remaining > 0)
            {
                _text.Append(buffer, 0, Math.Min(count, remaining));
            }
        }
    }

    private void SetState(ComparisonHostCaptureState state)
    {
        lock (_sync)
        {
            _state = state;
        }
    }

    private void MarkFaultedIfRunning()
    {
        lock (_sync)
        {
            if (_state == ComparisonHostCaptureState.Running)
            {
                _state = ComparisonHostCaptureState.Faulted;
            }
        }
    }

    internal ComparisonHostOutputCaptureSnapshot Snapshot()
    {
        lock (_sync)
        {
            var captured = _text.ToString();
            return new(_state, captured.Length,
                captured.Contains(MissingKeyLoadMarker, StringComparison.Ordinal),
                captured.Contains(MissingQdrantMarker, StringComparison.Ordinal),
                captured.Contains(InvalidDimensionsMarker, StringComparison.Ordinal));
        }
    }
}

internal enum ComparisonHostCaptureState
{
    Unavailable,
    Running,
    Completed,
    Canceled,
    Faulted
}

internal sealed record ComparisonHostOutputCaptureSnapshot(ComparisonHostCaptureState State, int Length,
    bool MissingKeyLoadSetting, bool MissingQdrantSetting, bool InvalidDimensions)
{
    internal bool IsCompleted => State == ComparisonHostCaptureState.Completed;

    internal static ComparisonHostOutputCaptureSnapshot Unavailable { get; } =
        new(ComparisonHostCaptureState.Unavailable, 0, false, false, false);
}
