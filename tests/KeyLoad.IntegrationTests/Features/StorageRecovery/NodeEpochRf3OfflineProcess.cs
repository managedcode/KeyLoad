using System.Diagnostics;

namespace KeyLoad.IntegrationTests.Features.StorageRecovery;

internal static class NodeEpochRf3OfflineProcess
{
    internal static async Task<NodeEpochRf3OfflineResult> RunAsync(ProcessStartInfo start,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        if (start.UseShellExecute || !start.RedirectStandardOutput || !start.RedirectStandardError)
        { throw new ArgumentException(NodeEpochRf3OfflineProtocol.FailedStart, nameof(start)); }
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = start };
        if (!process.Start())
        { throw new InvalidOperationException(NodeEpochRf3OfflineProtocol.FailedStart); }
        var output = NodeEpochRf3OfflineProcessIo.ReadAsync(process.StandardOutput.BaseStream);
        var error = NodeEpochRf3OfflineProcessIo.ReadAsync(process.StandardError.BaseStream);
        var exit = process.WaitForExitAsync(CancellationToken.None);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(NodeEpochRf3OfflineProtocol.Deadline);
        try
        {
            await NodeEpochRf3OfflineProcessIo.WaitAsync(exit, output, error, deadline.Token).ConfigureAwait(false);
            return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch (Exception primary)
        {
            var failures = await NodeEpochRf3OfflineProcessIo.SettleAsync(process, exit, output, error).ConfigureAwait(false);
            if (failures.Count > 0)
            { throw new AggregateException(new[] { primary }.Concat(failures)); }
            throw;
        }
    }
}
