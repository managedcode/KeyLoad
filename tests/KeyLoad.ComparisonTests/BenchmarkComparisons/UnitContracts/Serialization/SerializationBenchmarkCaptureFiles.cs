using System.Diagnostics;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Owns both capture files from pre-spawn acquisition through settled pump disposal.</summary>
internal sealed class SerializationBenchmarkCaptureFiles : IAsyncDisposable
{
    private readonly FileStream _stdout;
    private readonly FileStream _stderr;
    private readonly SerializationBenchmarkFailures _failures;

    internal SerializationBenchmarkCaptureFiles(string directory, SerializationBenchmarkFailures failures)
    {
        _failures = failures;
        try
        {
            _stdout = File.Create(Path.Combine(directory, "host.stdout.txt"));
        }
        catch (Exception failure) when (SerializationBenchmarkFailures.IsNonFatal(failure))
        {
            _failures.Add(failure);
            throw;
        }
        try
        {
            _stderr = File.Create(Path.Combine(directory, "host.stderr.txt"));
        }
        catch (Exception failure) when (SerializationBenchmarkFailures.IsNonFatal(failure))
        {
            _failures.Add(failure);
            try
            {
                _stdout.Dispose();
            }
            catch (Exception cleanup) when (SerializationBenchmarkFailures.IsNonFatal(cleanup))
            {
                _failures.Add(cleanup);
            }
            throw;
        }
    }

    internal async Task<int> RunAsync(Process process, CancellationToken cancellationToken)
        => await SerializationBenchmarkCaptures.RunAsync(process, _stdout, _stderr, cancellationToken);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _stderr.DisposeAsync();
        }
        catch (Exception failure) when (SerializationBenchmarkFailures.IsNonFatal(failure))
        {
            _failures.Add(failure);
        }
        try
        {
            await _stdout.DisposeAsync();
        }
        catch (Exception failure) when (SerializationBenchmarkFailures.IsNonFatal(failure))
        {
            _failures.Add(failure);
        }
        GC.SuppressFinalize(this);
    }
}
