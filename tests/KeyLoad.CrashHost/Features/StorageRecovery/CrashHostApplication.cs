using System.Globalization;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class CrashHostApplication
{
    internal static async Task RunAsync(string[] args)
    {
        if (await ExistingStoreInspector.TryRunAsync(args))
        {
            return;
        }
        if (await ReplicaCrashScenario.TryRunAsync(args))
        {
            return;
        }
        var directory = args[0];
        var stage = Enum.Parse<CommitStage>(args[1]);
        var mutationIndex = int.Parse(args[2], CultureInfo.InvariantCulture);
        var mode = args.Length > 3 ? args[3] : CrashFixtureValues.CommitMode;
        var boundary = new CanonicalCrashBoundary(stage, mutationIndex, mode == CrashFixtureValues.CommitMode);
        using var store = new ZoneTreeStore(new(directory) { FaultObserver = boundary.Observe });
        if (mode == CrashFixtureValues.ProjectionMode)
        {
            await ProjectionCrashScenario.RunAsync(directory, store, boundary);
        }
        else if (mode == CrashFixtureValues.SubscriptionMode)
        {
            await SubscriptionCrashScenario.RunAsync(directory, store, boundary);
        }
        else
        {
            CanonicalCrashScenario.Run(directory, store, boundary, mode);
            await CrashHostPause.WaitForKillAsync();
        }
    }
}

internal sealed class CrashHostMarker;
