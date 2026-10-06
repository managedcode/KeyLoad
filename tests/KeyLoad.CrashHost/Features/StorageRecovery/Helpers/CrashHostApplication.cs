using System.Globalization;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.CrashHost.Features.DocumentStorage;
using KeyLoad.CrashHost.Features.Search;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class CrashHostApplication
{
    internal static async Task RunAsync(string[] args)
    {
        const int ArgsFirstIndex = 0;
        const int ArgsSecondIndex = 1;
        const int ArgsComponentIndex = 2;
        const int ArgsLengthValidationBoundary = 3;
        const int RunAsyncArgsComponentIndex = 3;

        _ = SerializationExecutionRegistration.Process.Value;
        if (await EpochPriorSourceProbe.TryRunAsync(args) || await EpochUpgradeCrashScenario.TryRunAsync(args))
        {
            return;
        }
        if (await NodeEpochCrashScenario.TryRunAsync(args) || await NodeEpochRegularFileScenario.TryRunAsync(args)
            || await NativeTextCrashScenario.TryRunAsync(args))
        {
            return;
        }
        if (await C1OutcomeInspection.TryRunAsync(args) || await ExistingStoreInspector.TryRunAsync(args))
        {
            return;
        }
        if (await ReplicaCrashScenario.TryRunAsync(args))
        {
            return;
        }
        var directory = args[ArgsFirstIndex];
        var stage = Enum.Parse<CommitStage>(args[ArgsSecondIndex]);
        var mutationIndex = int.Parse(args[ArgsComponentIndex], CultureInfo.InvariantCulture);
        var mode = args.Length > ArgsLengthValidationBoundary ? args[RunAsyncArgsComponentIndex] : CrashFixtureValues.CommitMode;
        var boundary = new CanonicalCrashBoundary(stage, mutationIndex, mode == CrashFixtureValues.CommitMode);
        using var store = new ZoneTreeStore(new(directory) { FaultObserver = boundary.Observe }, CrashExecutionOptions.StorageExecution(), CrashExecutionOptions.PointCacheExecution());
        await RunScenarioAsync(directory, store, boundary, mode);
    }

    private static Task RunScenarioAsync(string directory, ZoneTreeStore store,
        CanonicalCrashBoundary boundary, string mode) => mode switch
        {
            CrashFixtureValues.ProjectionMode => ProjectionCrashScenario.RunAsync(directory, store, boundary),
            CrashFixtureValues.SubscriptionMode => SubscriptionCrashScenario.RunAsync(directory, store, boundary),
            DatabaseCompositionCrashScenario.Mode => DatabaseCompositionCrashScenario.RunAsync(directory, store, boundary),
            AggregateReplayCrashScenario.Mode => AggregateReplayCrashScenario.RunAsync(directory, store, boundary),
            SampleRetentionCrashScenario.Mode => SampleRetentionCrashScenario.RunAsync(directory, store, boundary),
            EventProjectionCrashScenario.Mode => EventProjectionCrashScenario.RunAsync(directory, store, boundary),
            RecurringScheduleCrashScenario.Mode => RecurringScheduleCrashScenario.RunAsync(directory, store, boundary),
            SagaTimeoutCrashScenario.Mode => SagaTimeoutCrashScenario.RunAsync(directory, store, boundary),
            CommandIdempotencyCrashContract.FirstMode => CommandIdempotencyCrashScenario.RunFirstAsync(directory, store),
            CommandIdempotencyCrashContract.ReplayMode => CommandIdempotencyCrashScenario.RunReplayAsync(directory, store),
            _ => RunCanonicalAsync(directory, store, boundary, mode)
        };

    private static async Task RunCanonicalAsync(string directory, ZoneTreeStore store,
        CanonicalCrashBoundary boundary, string mode)
    {
        CanonicalCrashScenario.Run(directory, store, boundary, mode);
        await CrashHostPause.WaitForKillAsync();
    }
}

internal sealed class CrashHostMarker;
