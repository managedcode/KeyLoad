using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>Owns both capture files from pre-spawn acquisition through settled pump disposal.</summary>
internal sealed class NativeSerializationBenchmarkCaptureFiles : IAsyncDisposable
{
    private readonly FileStream _stdout;
    private readonly FileStream _stderr;
    private readonly NativeSerializationBenchmarkFailures _failures;

    internal NativeSerializationBenchmarkCaptureFiles(string directory, NativeSerializationBenchmarkFailures failures)
    {
        _failures = failures;
        try
        {
            _stdout = File.Create(Path.Combine(directory, "host.stdout.txt"));
        }
        catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
        {
            _failures.Add(failure);
            throw;
        }
        try
        {
            _stderr = File.Create(Path.Combine(directory, "host.stderr.txt"));
        }
        catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
        {
            _failures.Add(failure);
            try
            {
                _stdout.Dispose();
            }
            catch (Exception cleanup) when (NativeSerializationBenchmarkFailures.IsNonFatal(cleanup))
            {
                _failures.Add(cleanup);
            }
            throw;
        }
    }

    internal async Task<int> RunAsync(Process process, CancellationToken cancellationToken)
        => await NativeSerializationBenchmarkCaptures.RunAsync(process, _stdout, _stderr, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _stderr.DisposeAsync();
        }
        catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
        {
            _failures.Add(failure);
        }
        try
        {
            await _stdout.DisposeAsync();
        }
        catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
        {
            _failures.Add(failure);
        }
        GC.SuppressFinalize(this);
    }
}
