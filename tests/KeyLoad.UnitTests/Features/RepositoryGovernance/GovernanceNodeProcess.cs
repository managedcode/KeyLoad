using System.Diagnostics;
using System.Text;

namespace KeyLoad.UnitTests.Features.RepositoryGovernance;

internal sealed record GovernanceResult(int ExitCode, string Output, string Error);

internal static class GovernanceNodeProcess
{
    private const int OutputLimitCharacters = 64 * 1024;
    private const int OutputReadBufferCharacters = 4096;
    private const int ExecutionDeadlineSeconds = 15;
    private const int CleanupDeadlineSeconds = 3;
    private const string SolutionFile = "KeyLoad.slnx";
    private const string NodeExecutable = "node";
    private const string ScriptDirectory = "scripts";
    private const string FeaturesDirectory = "Features";
    private const string GovernanceDirectory = "RepositoryGovernance";
    private const string ScriptFile = "verify.mjs";
    private const string RootOption = "--root";
    private const string MissingSolutionMessage = "KeyLoad.slnx was not found above the test assembly.";
    private const string ProcessStartMessage = "Node governance validator did not start.";
    private const string OutputLimitMessage = "Governance validator output exceeded its bound.";
    private static readonly TimeSpan Deadline = TimeSpan.FromSeconds(ExecutionDeadlineSeconds);

    internal static string RepositoryRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null;
                directory = directory.Parent)
            {
                if (File.Exists(Path.Combine(directory.FullName, SolutionFile)))
                {
                    return directory.FullName;
                }
            }
            throw new DirectoryNotFoundException(MissingSolutionMessage);
        }
    }

    internal static async Task<GovernanceResult> RunAsync(string fixtureRoot)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current!.Execution.CancellationToken);
        timeout.CancelAfter(Deadline);
        using var process = new Process { StartInfo = CreateStartInfo(fixtureRoot) };
        if (!process.Start())
        {
            throw new InvalidOperationException(ProcessStartMessage);
        }
        var output = ReadBoundedAsync(process.StandardOutput, timeout.Token);
        var error = ReadBoundedAsync(process.StandardError, timeout.Token);
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            var captured = await Task.WhenAll(output, error).WaitAsync(timeout.Token);
            return new GovernanceResult(process.ExitCode, captured[0], captured[1]);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(CleanupDeadlineSeconds));
            await process.WaitForExitAsync(cleanup.Token);
            await timeout.CancelAsync();
            try
            {
                await Task.WhenAll(output, error).WaitAsync(cleanup.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                // The process exit closes both owned readers after cancellation.
            }
        }
    }

    private static ProcessStartInfo CreateStartInfo(string fixtureRoot)
    {
        var start = new ProcessStartInfo(NodeExecutable)
        {
            WorkingDirectory = RepositoryRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        start.ArgumentList.Add(Path.Combine(RepositoryRoot, ScriptDirectory, FeaturesDirectory,
            GovernanceDirectory, ScriptFile));
        start.ArgumentList.Add(RootOption);
        start.ArgumentList.Add(fixtureRoot);
        return start;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader, CancellationToken token)
    {
        var output = new StringBuilder();
        var buffer = new char[OutputReadBufferCharacters];
        while (true)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(), token);
            if (count == 0)
            {
                return output.ToString();
            }
            if (output.Length + count > OutputLimitCharacters)
            {
                throw new InvalidOperationException(OutputLimitMessage);
            }
            output.Append(buffer, 0, count);
        }
    }
}
