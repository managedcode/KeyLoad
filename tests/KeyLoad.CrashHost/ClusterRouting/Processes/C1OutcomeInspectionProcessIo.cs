using System.Diagnostics;
using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ClusterRouting.Processes;

internal static class C1OutcomeInspectionProcessIo
{
    internal static void Kill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
        }
    }

    internal static async Task WriteInputAsync(Process child, ReadOnlyMemory<byte> payload)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(
            () => child.StandardInput.BaseStream.WriteAsync(payload).AsTask(), failures).ConfigureAwait(false);
        await ServerFailureObserver.ObserveAsync(
            child.StandardInput.BaseStream.FlushAsync, failures).ConfigureAwait(false);
        ServerFailureObserver.Observe(child.StandardInput.Close, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
