using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class CrashHostPause
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

    internal static async Task WaitForKillAsync()
    {
        await StandardOutput.WriteAsync(AcknowledgementLine);
        await StandardOutput.FlushAsync();
        await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System);
    }
}

internal sealed class CanonicalCrashBoundary(CommitStage stage, int mutationIndex, bool initiallyArmed)
{
    internal bool Armed { get; set; } = initiallyArmed;
    internal long Position { get; set; } = CrashFixtureValues.InitialCrashPosition;

    internal void Observe(CommitStage observed, long position, int index)
    {
        if (Armed && position == Position && observed == stage
            && (stage != CommitStage.MutationApplied || index == mutationIndex))
        {
            CrashHostPause.AtBoundary();
        }
    }
}
