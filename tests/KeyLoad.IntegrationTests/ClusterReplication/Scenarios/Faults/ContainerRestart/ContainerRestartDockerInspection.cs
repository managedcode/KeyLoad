using System.Diagnostics;

namespace KeyLoad.IntegrationTests.Features.ClusterReplication;

/// <summary>Runs one bounded read-only Docker inspection and retains only allowlisted fields.</summary>
internal static class ContainerRestartDockerInspection
{
    private static readonly TimeSpan DiagnosticTimeout = TimeSpan.FromSeconds(3);
    private const int MaximumOutputCharacters = 4_096;
    private const int MaximumDockerNameCharacters = 128;
    private const string DockerExecutable = "docker";
    private const string InspectCommand = "inspect";
    private const string FormatArgument = "--format";
    private const string InspectFormat = "{{.Id}}|{{.State.Status}}|{{.State.ExitCode}}|{{.State.OOMKilled}}|{{.State.StartedAt}}|{{.State.FinishedAt}}|{{.State.Error}}";
    private const string DockerUnavailableLine = "Docker restart inspection unavailable.";

    /// <summary>Runs the Docker CLI with a fixed format and independent three-second inspection deadline.</summary>
    /// <param name="containerName">The validated Docker container name from the native fixture model.</param>
    /// <returns>A line of validated values or a fixed unavailable marker.</returns>
    internal static async Task<string> ReadAsync(string containerName)
    {
        if (!IsSafeDockerName(containerName))
        {
            return DockerUnavailableLine;
        }

        using var timeout = new CancellationTokenSource(DiagnosticTimeout, TimeProvider.System);
        using var process = Process.Start(CreateStartInfo(containerName))
            ?? throw new InvalidOperationException(DockerUnavailableLine);
        return await InspectProcessAsync(process, timeout.Token).ConfigureAwait(false);
    }

    private static async Task<string> InspectProcessAsync(Process process, CancellationToken cancellationToken)
    {
        Task<string>? standardOutput = null;
        Task<string>? standardError = null;
        Task? processExit = null;
        var output = string.Empty;
        var processAndReadersObserved = false;
        try
        {
            standardOutput = ContainerRestartProcessIo.ReadBoundedAsync(
                process.StandardOutput, MaximumOutputCharacters, cancellationToken);
            standardError = ContainerRestartProcessIo.ReadBoundedAsync(process.StandardError, 0, cancellationToken);
            processExit = process.WaitForExitAsync(cancellationToken);
            if (!await ContainerRestartProcessIo.WaitForExitOrReaderFailureAsync(
                    processExit, standardOutput, standardError).ConfigureAwait(false))
            {
                return DockerUnavailableLine;
            }

            await processExit.ConfigureAwait(false);
            await Task.WhenAll(standardOutput, standardError).WaitAsync(cancellationToken).ConfigureAwait(false);
            output = await standardOutput.ConfigureAwait(false);
            processAndReadersObserved = true;
        }
        catch (Exception failure) when (ClusterReplicationTestSupport.IsNonFatalCleanupFailure(failure))
        {
            return DockerUnavailableLine;
        }
        finally
        {
            if (!processAndReadersObserved)
            {
                await CleanupAfterFailureAsync(process, processExit, standardOutput, standardError).ConfigureAwait(false);
            }
        }

        if (process.ExitCode != 0)
        {
            return DockerUnavailableLine;
        }

        return FormatInspection(output);
    }

    private static async Task CleanupAfterFailureAsync(Process process, Task? processExit,
        Task<string>? standardOutput, Task<string>? standardError)
    {
        try
        {
            await ContainerRestartProcessIo.KillAndObserveAsync(
                process, processExit, standardOutput, standardError).ConfigureAwait(false);
        }
        catch (Exception cleanupFailure) when (
            ClusterReplicationTestSupport.IsNonFatalCleanupFailure(cleanupFailure))
        {
        }
    }

    /// <summary>Projects a native inspect record to validated fields or one fixed unavailable marker.</summary>
    /// <param name="output">The bounded output from the actual Docker process.</param>
    /// <returns>A closed projection that excludes the native error text.</returns>
    internal static string FormatInspection(string output) => ContainerRestartDockerRecord.FormatInspection(output);

    /// <summary>Returns the validated full Docker id or a fixed unavailable marker.</summary>
    internal static string CanonicalContainerId(string value) => ContainerRestartDockerRecord.CanonicalContainerId(value);

    /// <summary>Returns whether the value is a full native hexadecimal resource identifier.</summary>
    internal static bool IsNativeHexIdentifier(string value) =>
        (value.Length is 32 or ContainerRuntimeProtocol.FullContainerIdCharacters) && value.All(Uri.IsHexDigit);

    /// <summary>Returns a closed Docker state or a fixed other marker.</summary>
    internal static string ClosedState(string value) => ContainerRestartDockerRecord.ClosedState(value);

    /// <summary>Returns a canonical UTC Docker timestamp or a fixed unavailable marker.</summary>
    internal static string ClosedTimestamp(string value) => ContainerRestartDockerRecord.ClosedTimestamp(value);

    private static ProcessStartInfo CreateStartInfo(string containerName)
    {
        var startInfo = new ProcessStartInfo(DockerExecutable)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add(InspectCommand);
        startInfo.ArgumentList.Add(FormatArgument);
        startInfo.ArgumentList.Add(InspectFormat);
        startInfo.ArgumentList.Add(containerName);
        return startInfo;
    }

    private static bool IsSafeDockerName(string value)
    {
        if (value.Length is 0 or > MaximumDockerNameCharacters || !IsAsciiAlphaNumeric(value[0]))
        {
            return false;
        }

        return value.All(static character => IsAsciiAlphaNumeric(character) || character is '_' or '.' or '-');
    }

    private static bool IsAsciiAlphaNumeric(char value) => value is >= 'a' and <= 'z'
        or >= 'A' and <= 'Z' or >= '0' and <= '9';

}
