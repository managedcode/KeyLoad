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

    internal static async Task WaitForKillAsync()
    {
        await WriteAcknowledgementAsync();
        await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System);
    }
}
