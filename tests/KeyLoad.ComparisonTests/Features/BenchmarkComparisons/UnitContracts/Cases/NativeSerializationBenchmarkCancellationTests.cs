using System.Diagnostics;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class NativeSerializationBenchmarkCancellationTests
{
    private const string NodeExecutable = "node";
    private const int StartupTimeoutSeconds = 5;
    private const int StartupPollMilliseconds = 25;
    private const string ChildSource = "import { writeFileSync } from 'node:fs'; writeFileSync(process.argv[1], 'started'); setInterval(() => {}, 1000);";

    [Test]
    public async Task CallerCancellationStopsRealChildAndSettlesCaptureStreams()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var directory = Path.Combine(Path.GetTempPath(), "keyload-native-cancel-" + Guid.NewGuid().ToString("N"));
        using var cancellation = new CancellationTokenSource();
        Task<int>? execution = null;
        var failures = new NativeSerializationBenchmarkFailures();
        try
        {
            Directory.CreateDirectory(directory);
            var marker = Path.Combine(directory, "child-started.txt");
            execution = NativeSerializationBenchmarkProcess.RunProcessAsync(CreateStartInfo(marker), directory, cancellation.Token);
            await WaitForStartAsync(marker, cancellationToken);
            await cancellation.CancelAsync();
            await Assert.That(await WasCancelledAsync(execution)).IsTrue();
            await Assert.That(File.Exists(Path.Combine(directory, "host.stdout.txt"))).IsTrue();
            await Assert.That(File.Exists(Path.Combine(directory, "host.stderr.txt"))).IsTrue();
        }
        catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        finally
        {
            await FinishOwnedExecutionAsync(execution, cancellation, directory, failures);
        }

        failures.ThrowIfAny();
    }

    private static ProcessStartInfo CreateStartInfo(string marker)
    {
        var startInfo = new ProcessStartInfo(NodeExecutable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        startInfo.ArgumentList.Add("--input-type=module");
        startInfo.ArgumentList.Add("-e");
        startInfo.ArgumentList.Add(ChildSource);
        startInfo.ArgumentList.Add(marker);
        return startInfo;
    }

    private static async Task WaitForStartAsync(string marker, CancellationToken cancellationToken)
    {
        using var deadlineTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(StartupTimeoutSeconds), TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineTimeout.Token);
        while (!await HasStartedAsync(marker, deadline.Token))
        {
            await Task.Delay(TimeSpan.FromMilliseconds(StartupPollMilliseconds), TimeProvider.System, deadline.Token);
        }
    }

    private static async Task<bool> HasStartedAsync(string marker, CancellationToken cancellationToken)
        => File.Exists(marker) && await File.ReadAllTextAsync(marker, cancellationToken) == "started";

    private static async Task<bool> WasCancelledAsync(Task<int> execution)
    {
        try
        {
            _ = await execution;
            return false;
        }
        catch (OperationCanceledException)
        {
            return true;
        }
    }

    private static async Task FinishOwnedExecutionAsync(Task<int>? execution, CancellationTokenSource cancellation,
        string directory, NativeSerializationBenchmarkFailures failures)
    {
        try
        {
            if (!cancellation.IsCancellationRequested)
            {
                await cancellation.CancelAsync();
            }
        }
        catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
        {
            failures.Add(failure);
        }
        finally
        {
            if (execution is not null)
            {
                await ObserveExecutionAsync(execution, failures);
            }
            if (execution?.IsCompleted != false && Directory.Exists(directory))
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
                {
                    failures.Add(failure);
                }
            }
        }
    }

    private static async Task ObserveExecutionAsync(Task<int> execution, NativeSerializationBenchmarkFailures failures)
    {
        try
        {
            _ = await execution.WaitAsync(TimeSpan.FromSeconds(15), TimeProvider.System);
        }
        catch (OperationCanceledException) when (execution.IsCanceled)
        {
        }
        catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
        {
            failures.AddTaskFailures(execution, failure);
        }
        finally
        {
            try
            {
                _ = await execution;
            }
            catch (OperationCanceledException) when (execution.IsCanceled)
            {
            }
            catch (Exception failure) when (NativeSerializationBenchmarkFailures.IsNonFatal(failure))
            {
                failures.AddTaskFailures(execution, failure);
            }
        }
    }
}
