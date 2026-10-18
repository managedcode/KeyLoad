using System.Diagnostics;
using System.Globalization;
using KeyLoad.AppHost.Features.TestInfrastructure;
using Microsoft.Extensions.Options;

namespace KeyLoad.AppHost.Features.ClusterReplication;

/// <summary>Proves the selected runtime belongs to its original native launch process.</summary>
internal static class ProjectProcessParent
{
    private const int MaximumAncestors = 32;
    private const int InitialAncestor = 0;
    private const int MissingProcessId = 0;
    private const string UnsupportedOperatingSystem = "Native project process ownership requires a supported operating system.";
    private const string ProcessStatusExecutable = "/bin/ps";
    private const string ProcessSelectionArgument = "-p";
    private const string OutputSelectionArgument = "-o";
    private const string ParentProcessColumn = "ppid=";

    internal static async Task RequireOwnedAsync(int processId, int launchProcessId,
        IOptions<TestExecutionOptions> options, CancellationToken token)
    {
        using var timeout = new CancellationTokenSource(options.Value.ProcessSettlementTimeout, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token, timeout.Token);
        var current = processId;
        for (var depth = InitialAncestor; depth < MaximumAncestors; depth++)
        {
            deadline.Token.ThrowIfCancellationRequested();
            if (current == launchProcessId)
            {
                return;
            }
            var parent = OperatingSystem.IsWindows()
                ? ProjectProcessParentWindows.Read(current, children: false, options, deadline.Token).Single()
                : await ReadUnixAsync(current, options, deadline.Token).ConfigureAwait(false);
            if (parent <= MissingProcessId || parent == current)
            {
                break;
            }
            current = parent;
        }
        throw new InvalidOperationException(ProcessFaultCommandProtocol.Refused);
    }

    private static async Task<int> ReadUnixAsync(int processId, IOptions<TestExecutionOptions> options, CancellationToken token)
    {
        if (!OperatingSystem.IsLinux() && !OperatingSystem.IsMacOS())
        {
            throw new PlatformNotSupportedException(UnsupportedOperatingSystem);
        }
        var start = new ProcessStartInfo(ProcessStatusExecutable);
        start.ArgumentList.Add(ProcessSelectionArgument);
        start.ArgumentList.Add(processId.ToString(CultureInfo.InvariantCulture));
        start.ArgumentList.Add(OutputSelectionArgument);
        start.ArgumentList.Add(ParentProcessColumn);
        var output = await ProjectProcessObservationCommand.ReadAsync(start, options, options.Value.ProcessSettlementTimeout, token)
            .ConfigureAwait(false);
        if (!int.TryParse(output.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var parent))
        {
            throw new InvalidOperationException(ProcessFaultCommandProtocol.Refused);
        }
        return parent;
    }
}
