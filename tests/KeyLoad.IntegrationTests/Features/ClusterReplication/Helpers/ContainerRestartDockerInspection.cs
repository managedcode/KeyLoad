using System.Diagnostics;
using System.Globalization;
using System.Text;

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

/// <summary>Parses and projects only selected native Docker inspection values.</summary>
internal static class ContainerRestartDockerRecord
{
    private const char InspectSeparator = '|';
    private const int InspectFieldCount = 7;
    private const string UnknownValue = "unavailable";
    private const string OtherValue = "other";
    private const string DockerUnavailableLine = "Docker restart inspection unavailable.";
    private const string DockerInspectFormat = "restart Docker id={0} state={1} exit={2} oomKilled={3} startedAt={4} finishedAt={5} hasError={6}";
    private static readonly CompositeFormat DockerInspectCompositeFormat = CompositeFormat.Parse(DockerInspectFormat);

    internal static string FormatInspection(string output)
    {
        if (!TryParse(output, out var inspection))
        {
            return DockerUnavailableLine;
        }

        return string.Format(CultureInfo.InvariantCulture, DockerInspectCompositeFormat, inspection.Id,
            inspection.State, inspection.ExitCode, inspection.OomKilled, inspection.StartedAt,
            inspection.FinishedAt, inspection.HasError);
    }

    internal static string CanonicalContainerId(string value) => IsValidContainerId(value) ? value : UnknownValue;

    internal static string ClosedState(string value) => TryState(value, out var state) ? state : OtherValue;

    internal static string ClosedTimestamp(string value) => TryTimestamp(value, out var timestamp) ? timestamp : UnknownValue;

    private static bool TryParse(string output, out DockerInspection inspection)
    {
        var fields = output.Trim().Split(InspectSeparator, InspectFieldCount, StringSplitOptions.None);
        if (fields.Length != InspectFieldCount || !IsValidContainerId(fields[0])
            || !TryState(fields[1], out var state)
            || !int.TryParse(fields[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out var exitCode)
            || exitCode < 0
            || !bool.TryParse(fields[3], out var oomKilled)
            || !TryTimestamp(fields[4], out var startedAt)
            || !TryTimestamp(fields[5], out var finishedAt))
        {
            inspection = default;
            return false;
        }

        inspection = new(fields[0], state, exitCode, oomKilled, startedAt, finishedAt,
            fields[6].Length > 0);
        return true;
    }

    private static bool IsValidContainerId(string value) =>
        value.Length == ContainerRuntimeProtocol.FullContainerIdCharacters && value.All(Uri.IsHexDigit);

    private static bool TryState(string value, out string state)
    {
        state = value switch
        {
            "created" => "created",
            "restarting" => "restarting",
            "running" => "running",
            "removing" => "removing",
            "paused" => "paused",
            "exited" => "exited",
            "dead" => "dead",
            _ => OtherValue
        };
        return state != OtherValue;
    }

    private static bool TryTimestamp(string value, out string timestamp)
    {
        var hasExplicitUtcOffset = value.EndsWith('Z') || value.EndsWith("+00:00", StringComparison.Ordinal);
        if (hasExplicitUtcOffset
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            && parsed.Offset == TimeSpan.Zero)
        {
            timestamp = parsed.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
            return true;
        }

        timestamp = UnknownValue;
        return false;
    }

    private readonly record struct DockerInspection(string Id, string State, int ExitCode, bool OomKilled,
        string StartedAt, string FinishedAt, bool HasError);
}
