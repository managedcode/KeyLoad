using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class EpochUpgradeCrashScenario
{
    internal const string Mode = "offline-data-epoch-upgrade";
    private const int ArgumentCount = 4;
    private const int SourceArgument = 0;
    private const int DestinationArgument = 1;
    private const int StageArgument = 2;
    private const int ModeArgument = 3;
    private const string UnexpectedStageMessage = "The requested offline-upgrade stage was not reached.";

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);
        if (args.Length != ArgumentCount || !string.Equals(args[ModeArgument], Mode, StringComparison.Ordinal))
        {
            return false;
        }

        await RunAsync(args[SourceArgument], args[DestinationArgument], args[StageArgument]);
        return true;
    }

    private static Task RunAsync(string sourceDirectory, string destinationDirectory, string stageText)
    {
        var requested = Enum.Parse<CommitStage>(stageText, ignoreCase: false);
        if (!Enum.IsDefined(requested) || requested < CommitStage.UpgradeSourceVerified
            || requested > CommitStage.UpgradePublished)
        {
            throw new ArgumentOutOfRangeException(nameof(stageText), stageText,
                "The offline-upgrade crash stage is unsupported.");
        }

        var boundary = new EpochUpgradeCrashBoundary(requested);
        var options = new ZoneTreeStoreOptions(destinationDirectory)
        {
            FaultObserver = (stage, _, _) => boundary.Observe(stage)
        };
        _ = ZoneTreeFormatUpgrade.Upgrade(sourceDirectory, options);
        if (!boundary.Observed)
        {
            throw new InvalidOperationException(UnexpectedStageMessage);
        }
        return Task.CompletedTask;
    }
}

internal sealed class EpochUpgradeCrashBoundary(CommitStage requestedStage)
{
    private int observed;
    internal bool Observed => Volatile.Read(ref observed) != 0;

    internal void Observe(CommitStage observedStage)
    {
        if (observedStage != requestedStage)
        {
            return;
        }

        if (Interlocked.Exchange(ref observed, 1) != 0)
        {
            throw new InvalidOperationException("The requested offline-upgrade stage was observed more than once.");
        }
        CrashHostPause.AtBoundary();
    }
}
