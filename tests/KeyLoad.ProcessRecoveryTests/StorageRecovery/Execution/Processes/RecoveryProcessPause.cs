using KeyLoad.Storage;

namespace KeyLoad.ProcessRecoveryTests;

internal static class RecoveryProcessPause
{
    private static readonly byte[] CrashMarkerLine =
        System.Text.Encoding.UTF8.GetBytes(CrashFixtureValues.CrashMarker + Environment.NewLine);
    private static readonly byte[] AcknowledgementLine =
        System.Text.Encoding.UTF8.GetBytes(CrashFixtureValues.Acknowledgement + Environment.NewLine);
    private static readonly Stream StandardOutput = Console.OpenStandardOutput();

    internal static void AtBoundary()
    {
        StandardOutput.Write(CrashMarkerLine);
        StandardOutput.Flush();
        using var paused = new ManualResetEventSlim(false);
        paused.Wait();
    }

    internal static async Task WriteAcknowledgementAsync()
    {
        await StandardOutput.WriteAsync(AcknowledgementLine);
        await StandardOutput.FlushAsync();
    }

    /// <summary>Acknowledges the completed operation once ZoneTree's write-ahead log holds it, then waits for the kill.</summary>
    internal static async Task WaitForKillAsync(IAtomicStore store)
    {
        await store.SyncAsync();
        await WriteAcknowledgementAsync();
        await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System);
    }
}
