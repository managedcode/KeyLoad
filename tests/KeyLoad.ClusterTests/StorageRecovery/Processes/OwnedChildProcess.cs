using System.Diagnostics;

namespace KeyLoad.ClusterTests.StorageRecovery;

internal static class OwnedChildProcess
{
    internal static async Task<OwnedChildProcessResult> RunAsync(ProcessStartInfo start,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(start);
        if (start.UseShellExecute || !start.RedirectStandardOutput || !start.RedirectStandardError)
        {
            throw new ArgumentException(OwnedChildProcessProtocol.FailedStart, nameof(start));
        }
        cancellationToken.ThrowIfCancellationRequested();
        using var process = new Process { StartInfo = start };
        if (!process.Start())
        {
            throw new InvalidOperationException(OwnedChildProcessProtocol.FailedStart);
        }
        var output = OwnedChildProcessIo.ReadAsync(process.StandardOutput.BaseStream);
        var error = OwnedChildProcessIo.ReadAsync(process.StandardError.BaseStream);
        var exit = process.WaitForExitAsync(CancellationToken.None);
        using var deadlineTimeout = new CancellationTokenSource(OwnedChildProcessProtocol.Deadline, TimeProvider.System);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadlineTimeout.Token);
        try
        {
            await OwnedChildProcessIo.WaitAsync(exit, output, error, deadline.Token).ConfigureAwait(false);
            return new(process.ExitCode, await output.ConfigureAwait(false), await error.ConfigureAwait(false));
        }
        catch (Exception primary)
        {
            var failures = await OwnedChildProcessIo.SettleAsync(process, exit, output, error).ConfigureAwait(false);
            failures.RemoveAll(failure => ReferenceEquals(failure, primary));
            if (failures.Count > 0)
            {
                throw new AggregateException(new[] { primary }.Concat(failures));
            }
            throw;
        }
    }
}
