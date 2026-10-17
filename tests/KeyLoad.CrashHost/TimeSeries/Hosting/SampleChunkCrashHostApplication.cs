namespace KeyLoad.CrashHost;

/// <summary>Composes the closed chunk crash mode and original owning CrashHost runner.</summary>
internal static class SampleChunkCrashHostApplication
{
    internal static async Task RunAsync(string[] args)
    {
        if (!await SampleChunkCrashScenario.TryRunAsync(args))
        {
            await CrashHostApplication.RunAsync(args);
        }
    }
}
