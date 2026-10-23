using System.Diagnostics;
using System.Globalization;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.TestInfrastructure;

/// <summary>Finds the exact SDK executable only among the original Aspire launcher's direct children.</summary>
internal static class ProjectProcessChild
{
    private const string ProcessSelectionExecutable = "/usr/bin/pgrep";
    private const string ParentSelectionArgument = "-P";
    private const string UnsupportedOperatingSystem = "Execution project process ownership requires a supported operating system.";

    internal static async Task<int> ReadAsync(int launchProcessId, string executable,
        IOptions<TestExecutionOptions> options, CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(options.Value.ProcessSettlementTimeout, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        var children = OperatingSystem.IsWindows()
            ? ProjectProcessParentWindows.Read(launchProcessId, children: true, options, deadline.Token)
            : await ReadUnixAsync(launchProcessId, options, deadline.Token).ConfigureAwait(false);
        var matches = new List<int>();
        foreach (var child in children)
        {
            deadline.Token.ThrowIfCancellationRequested();
            await ProjectProcessParent.RequireOwnedAsync(child, launchProcessId, options, deadline.Token).ConfigureAwait(false);
            using var process = Process.GetProcessById(child);
            if (process.HasExited)
            {
                throw new InvalidOperationException(ProcessFaultCommandProtocol.Refused);
            }
            if (string.Equals(process.MainModule?.FileName, executable, StringComparison.Ordinal))
            {
                matches.Add(child);
            }
        }
        if (matches.Count != 1)
        {
            throw new InvalidOperationException(ProcessFaultCommandProtocol.Refused);
        }
        return matches.Single();
    }

    private static async Task<int[]> ReadUnixAsync(int launchProcessId, IOptions<TestExecutionOptions> options,
        CancellationToken token)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(UnsupportedOperatingSystem);
        }
        var start = new ProcessStartInfo(ProcessSelectionExecutable);
        start.ArgumentList.Add(ParentSelectionArgument);
        start.ArgumentList.Add(launchProcessId.ToString(CultureInfo.InvariantCulture));
        var output = await ProjectProcessObservationCommand.ReadAsync(start, options, options.Value.ProcessSettlementTimeout, token)
            .ConfigureAwait(false);
        var children = output.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Select(value => int.Parse(value, NumberStyles.None, CultureInfo.InvariantCulture)).ToArray();
        if (children.Any(child => child <= 0) || children.Distinct().Count() != children.Length)
        {
            throw new InvalidOperationException(ProcessFaultCommandProtocol.Refused);
        }
        return children;
    }
}
