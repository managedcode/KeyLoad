using System.Diagnostics;
using System.Text.Json;
using KeyLoad.AppHost.Features.TestInfrastructure;
using KeyLoad.AppHost.Features.TestInfrastructure.Processes;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.RepositoryGovernance;
using Microsoft.Extensions.Options;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class WholeClassClusterAdmissionProcess
{
    private const string Node = "node";
    private const string InputMode = "--input-type=module";
    private const string EvaluationMode = "-e";
    private const string RelativeModule = "scripts/Features/TestInfrastructure/run-tests.mjs";
    private const string PathVariable = "PATH";
    private const string StartFailure = "The whole-class admission Node process did not start.";
    private const int TimeoutSeconds = 15;
    private const int SuccessExit = 0;

    internal static async Task<JsonElement> ReadAsync(string filter, CancellationToken token)
    {
        JsonElement? result = null;
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var process = new Process { StartInfo = StartInfo(filter) };
            await ServerFailureObserver.ObserveAsync(async () => result = await RunAsync(process, token), failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
        return result ?? throw new InvalidOperationException(StartFailure);
    }

    private static ProcessStartInfo StartInfo(string filter)
    {
        var root = GovernanceNodeProcess.RepositoryRoot;
        var start = new ProcessStartInfo(Node)
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in new[] { InputMode, EvaluationMode, WholeClassClusterAdmissionProgram.Source,
            Path.Combine(root, RelativeModule), filter })
        {
            start.ArgumentList.Add(argument);
        }
        var path = Environment.GetEnvironmentVariable(PathVariable);
        start.Environment.Clear();
        if (path is not null)
        {
            start.Environment[PathVariable] = path;
        }
        return start;
    }

    private static async Task<JsonElement> RunAsync(Process process, CancellationToken token)
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(TimeoutSeconds), TimeProvider.System);
        using var joined = CancellationTokenSource.CreateLinkedTokenSource(token, deadline.Token);
        if (!process.Start())
        {
            throw new InvalidOperationException(StartFailure);
        }
        var policy = Options.Create(new TestExecutionOptions());
        var exit = process.WaitForExitAsync(CancellationToken.None);
        var output = LocalClusterOwnedProcessLifetime.ReadBoundedAsync(process.StandardOutput, policy.Value.CleanupOutputCharacters);
        var error = LocalClusterOwnedProcessLifetime.ReadBoundedAsync(process.StandardError, policy.Value.CleanupOutputCharacters);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => LocalClusterOwnedProcessLifetime.ObserveAsync(
            exit, output, error, TimeProvider.System, joined.Token), failures);
        if (failures.Count != SuccessExit)
        {
            await LocalClusterOwnedProcessLifetime.TerminateAndJoinAsync(process, exit, output, error,
                failures, policy, TimeProvider.System);
        }
        ServerFailureObserver.ThrowIfAny(failures);
        await Assert.That(process.ExitCode).IsEqualTo(SuccessExit);
        await Assert.That(await error).IsEmpty();
        using var document = JsonDocument.Parse(await output);
        return document.RootElement.Clone();
    }
}
