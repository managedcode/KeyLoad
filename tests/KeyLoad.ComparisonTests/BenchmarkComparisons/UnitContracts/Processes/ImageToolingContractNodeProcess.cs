using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal static class ImageToolingContractNodeProcess
{
    private const string NodeCommand = "node";
    private const string NodeModuleRootEnvironment = "KEYLOAD_IMAGE_MODULE_ROOT";
    private const string PathEnvironment = "PATH";
    private const string NodeArgumentsMode = "--input-type=module";
    private const string NodeArgumentsEval = "-e";
    private const string SuccessProperty = "ok";
    private const string ValueProperty = "value";
    private const string MessageProperty = "message";
    private const string ProbeFailureMessage = "The image tooling Node contract probe failed.";
    private const string InputLimitMessage = "The image tooling Node contract input exceeded its bound.";
    private const int ProcessTimeoutSeconds = 10;
    private const int CleanupTimeoutSeconds = 2;
    private const int MaximumInputBytes = 64 * 1024;
    private const int MaximumOutputCharacters = 64 * 1024;

    internal static async Task<ImageToolingContractNodeResult> RunAsync(object request,
        CancellationToken cancellationToken)
    {
        var json = JsonSerializer.Serialize(request);
        if (JsonSerializer.SerializeToUtf8Bytes(request).Length > MaximumInputBytes)
        {
            throw new InvalidOperationException(InputLimitMessage);
        }

        var repository = RepositoryRoot();
        var moduleRoot = Path.Combine(repository, "scripts", "Features", "BenchmarkComparisons");
        using var process = new Process { StartInfo = CreateStartInfo(repository, moduleRoot) };
        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException(ProbeFailureMessage);
            }
        }
        catch (Exception)
        {
            throw new InvalidOperationException(ProbeFailureMessage);
        }

        using var deadlineTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(ProcessTimeoutSeconds), TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineTimeout.Token);
        var stdoutTask = ReadBoundedAsync(process.StandardOutput, deadline.Token);
        var stderrTask = ReadBoundedAsync(process.StandardError, deadline.Token);
        try
        {
            await process.StandardInput.WriteLineAsync(json.AsMemory(), deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            var output = await Task.WhenAll(stdoutTask, stderrTask).WaitAsync(deadline.Token);
            if (process.ExitCode != 0 || output[1].Length != 0)
            {
                throw new InvalidOperationException(ProbeFailureMessage);
            }

            return ParseResult(output[0]);
        }
        catch (Exception)
        {
            KillOwnedProcess(process);
            await ObserveAsync(stdoutTask);
            await ObserveAsync(stderrTask);
            throw;
        }
    }

    private static ImageToolingContractNodeResult ParseResult(string output)
    {
        using var document = JsonDocument.Parse(output);
        var root = document.RootElement;
        var succeeded = root.GetProperty(SuccessProperty).GetBoolean();
        var value = succeeded ? root.GetProperty(ValueProperty).Clone() : default;
        var message = succeeded ? string.Empty : root.GetProperty(MessageProperty).GetString() ?? string.Empty;
        return new(succeeded, value, message);
    }

    private static ProcessStartInfo CreateStartInfo(string repository, string moduleRoot)
    {
        var start = new ProcessStartInfo(NodeCommand)
        {
            WorkingDirectory = repository,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add(NodeArgumentsMode);
        start.ArgumentList.Add(NodeArgumentsEval);
        start.ArgumentList.Add(ImageToolingContractNodeProgram.Source);
        var path = Environment.GetEnvironmentVariable(PathEnvironment);
        start.Environment.Clear();
        if (!string.IsNullOrWhiteSpace(path))
        {
            start.Environment[PathEnvironment] = path;
        }

        start.Environment[NodeModuleRootEnvironment] = moduleRoot;
        return start;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken cancellationToken)
    {
        var buffer = new char[4096];
        var output = new StringBuilder();
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (count == 0)
            {
                return output.ToString();
            }

            if (output.Length + count > MaximumOutputCharacters)
            {
                throw new InvalidOperationException(ProbeFailureMessage);
            }
            output.Append(buffer, 0, count);
        }
    }

    private static void KillOwnedProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: false);
            }
        }
        catch (InvalidOperationException)
        {
            // The owned Node process can exit while the timeout path is observing it.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The owned process may become unavailable while its exit is observed.
        }
    }

    private static async Task ObserveAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(CleanupTimeoutSeconds), TimeProvider.System);
        }
        catch (OperationCanceledException)
        {
            // The bounded reader was canceled after its owned child was stopped.
        }
        catch (IOException)
        {
            // The reader can close while the owned child is being reaped.
        }
        catch (ObjectDisposedException)
        {
            // The reader can be disposed at the bounded cleanup limit.
        }
        catch (InvalidOperationException exception) when (exception.Message == ProbeFailureMessage)
        {
            // The bounded reader rejected excessive output with its fixed safe error.
        }
        catch (TimeoutException)
        {
            _ = task.ContinueWith(static completed => _ = completed.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }
    }

    private static string RepositoryRoot()
    {
        foreach (var path in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            for (var directory = new DirectoryInfo(path); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, "KeyLoad.slnx"))
                    && File.Exists(Path.Combine(directory.FullName, "scripts", "Features", "BenchmarkComparisons", "image-inputs.mjs")))
                {
                    return directory.FullName;
                }
            }
        }

        throw new DirectoryNotFoundException(ProbeFailureMessage);
    }
}
