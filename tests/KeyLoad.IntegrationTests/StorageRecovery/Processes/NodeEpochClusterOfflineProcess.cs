using System.Diagnostics;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NodeEpochClusterOfflineProcess
{
    internal static async Task<NodeEpochClusterOfflineResult> RunAsync(ProcessStartInfo start,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        if (start.UseShellExecute || !start.RedirectStandardOutput || !start.RedirectStandardError)
        {
            throw new ArgumentException(NodeEpochClusterOfflineProtocol.FailedStart, nameof(start));
        }
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            throw new InvalidOperationException(NodeEpochClusterOfflineProtocol.FailedStart);
        }
        var output = NodeEpochClusterOfflineProcessIo.ReadAsync(process.StandardOutput.BaseStream);
        var error = NodeEpochClusterOfflineProcessIo.ReadAsync(process.StandardError.BaseStream);
        var exit = process.WaitForExitAsync(CancellationToken.None);
        using var deadlineTimeout = new CancellationTokenSource(NodeEpochClusterOfflineProtocol.Deadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineTimeout.Token);
        try
        {
            await NodeEpochClusterOfflineProcessIo.WaitAsync(exit, output, error, deadline.Token).ConfigureAwait(false);
            return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch (Exception primary)
        {
            var failures = await NodeEpochClusterOfflineProcessIo.SettleAsync(process, exit, output, error).ConfigureAwait(false);
            failures.RemoveAll(failure => ReferenceEquals(failure, primary));
            if (failures.Count > 0)
            {
                throw new AggregateException(new[] { primary }.Concat(failures));
            }
            throw;
        }
    }
}
