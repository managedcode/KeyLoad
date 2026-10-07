using System.Diagnostics;
using System.Globalization;
using System.Runtime.ExceptionServices;
using KeyLoad.IntegrationTests.Features.ClusterReplication.Processes;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Reads actual Docker runtime identity and polls the original kill/restart state transitions.</summary>
internal static class ContainerRuntimeDocker
{
    private const int MaximumOutputCharacters = 16_384;
    internal static async Task<ContainerRuntimeInspection> WaitForExitedAsync(string containerName,
        string resourceName, CancellationToken cancellationToken)
    {
        var clock = TimeProvider.System;
        var deadline = clock.GetTimestamp();
        ContainerRuntimeInspection stopped;
        do
        {
            stopped = await InspectAsync(containerName, cancellationToken);
            if (stopped.State == ContainerRuntimeProtocol.ExitedState)
            {
                break;
            }
            await Task.Delay(ContainerRuntimeProtocol.ExitPollInterval, clock, cancellationToken);
        }
        while (clock.GetElapsedTime(deadline) < ContainerRuntimeProtocol.ContainerExitTimeout);

        if (stopped.State != ContainerRuntimeProtocol.ExitedState)
        {
            throw new TimeoutException(string.Format(CultureInfo.InvariantCulture,
                ContainerRuntimeProtocol.ExitFailure, resourceName));
        }
        return stopped;
    }

    internal static async Task<ContainerRuntimeInspection> InspectRunningAsync(string containerName,
        string resourceName, CancellationToken cancellationToken)
    {
        var clock = TimeProvider.System;
        var deadline = clock.GetTimestamp();
        while (clock.GetElapsedTime(deadline) < ContainerRuntimeProtocol.ContainerStartTimeout)
        {
            var current = await InspectAsync(containerName, cancellationToken);
            if (current.State == ContainerRuntimeProtocol.RunningState)
            {
                return current;
            }
            await Task.Delay(ContainerRuntimeProtocol.StartPollInterval, clock, cancellationToken);
        }
        throw new TimeoutException(string.Format(CultureInfo.InvariantCulture,
            ContainerRuntimeProtocol.RunningFailure, resourceName));
    }

    internal static async Task<ContainerRuntimeInspection> InspectAsync(string containerName, CancellationToken cancellationToken)
    {
        var result = await RunAsync([ContainerRuntimeProtocol.InspectCommand, ContainerRuntimeProtocol.FormatArgument,
            ContainerRuntimeProtocol.InspectFormat, containerName], cancellationToken);
        EnsureSuccessful(result, ContainerRuntimeProtocol.InspectCommand, containerName);
        var fields = result.StandardOutput.Trim().Split(ContainerRuntimeProtocol.InspectSeparator);
        if (fields.Length != ContainerRuntimeProtocol.InspectFieldCount || !IsFullContainerId(fields[ContainerRuntimeProtocol.IdField]))
        {
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture,
                ContainerRuntimeProtocol.InspectFailure, containerName));
        }
        return new(fields[ContainerRuntimeProtocol.IdField], fields[ContainerRuntimeProtocol.ConfigImageField],
            fields[ContainerRuntimeProtocol.ImageIdField], fields[ContainerRuntimeProtocol.StateField], fields[ContainerRuntimeProtocol.StartedAtField]);
    }

    internal static async Task<ContainerRuntimeProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo(ContainerRuntimeProtocol.DockerExecutable)
        { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }
        var process = Process.Start(startInfo) ?? throw new InvalidOperationException(ContainerRuntimeProtocol.DockerStartFailure);
        return await RunOwnedAsync(process, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<ContainerRuntimeProcessResult> RunOwnedAsync(Process process, CancellationToken cancellationToken)
    {
        Task? exit = null;
        Task<string>? output = null;
        Task<string>? error = null;
        var failures = new List<Exception>();
        var original = RunProcessAsync();
        try
        {
            var primary = await OwnedProcessFailureObserver.CaptureAsync(original).ConfigureAwait(false);
            if (primary is not null)
            {
                failures.Add(primary);
                var cleanup = LocalImageOwnedProcessLifetime.TerminateAndJoinAsync(process, exit, output, error, failures);
                var cleanupFailure = await OwnedProcessFailureObserver.CaptureAsync(cleanup).ConfigureAwait(false);
                if (cleanupFailure is not null)
                { failures.Add(cleanupFailure); }
            }
        }
        finally
        {
            OwnedProcessFailureObserver.Observe(process.Dispose, failures);
        }
        if (failures.Count == 1)
        { ExceptionDispatchInfo.Capture(failures[0]).Throw(); }
        if (failures.Count > 1)
        { throw new AggregateException("Docker CLI execution and owned process settlement failed.", failures); }
        return await original.ConfigureAwait(false);

        async Task<ContainerRuntimeProcessResult> RunProcessAsync()
        {
            exit = process.WaitForExitAsync(CancellationToken.None);
            output = LocalImageOwnedProcessLifetime.ReadBoundedAsync(process.StandardOutput, MaximumOutputCharacters);
            error = LocalImageOwnedProcessLifetime.ReadBoundedAsync(process.StandardError, MaximumOutputCharacters);
            await LocalImageOwnedProcessLifetime.ObserveAsync(exit, output, error, cancellationToken).ConfigureAwait(false);
            return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
    }

    internal static void EnsureSuccessful(ContainerRuntimeProcessResult result, string operation, string target)
    {
        if (result.ExitCode != ContainerRuntimeProtocol.SuccessfulExitCode)
        {
            throw new InvalidOperationException(string.Format(CultureInfo.InvariantCulture, ContainerRuntimeProtocol.DockerFailure,
                operation, target, result.ExitCode, Clip(result.StandardError)));
        }
    }

    internal static string Clip(string value) => value.Length <= ContainerRuntimeProtocol.DiagnosticCharacters
        ? value : value[..ContainerRuntimeProtocol.DiagnosticCharacters];

    private static bool IsFullContainerId(string value) => value.Length == ContainerRuntimeProtocol.FullContainerIdCharacters && value.All(Uri.IsHexDigit);
}
