using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Server;
using P = KeyLoad.CrashHost.Features.AnalyticsStore.UserAnalyticsProcessProtocol;

namespace KeyLoad.CrashHost.Features.AnalyticsStore;

internal static class UserAnalyticsProcessScenario
{
    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length != P.ArgumentCount || args[P.ModeArgument] is not (P.Prepare or P.Recover))
        {
            return false;
        }

        var root = args[P.RootArgument];
        using var timeout = new CancellationTokenSource(CrashExecutionOptions.Child().Value.InspectionExecutionTimeout, TimeProvider.System);
        await UserAnalyticsProcessHost.RunAsync(root, (host, options) => args[P.ModeArgument] == P.Prepare
            ? PrepareAsync(root, host, timeout.Token) : RecoverAsync(root, host, options, timeout.Token), timeout.Token);
        return true;
    }

    private static async Task PrepareAsync(string root, PartitionHost host, CancellationToken token)
    {
        UserAnalyticsProcessMutations.Bootstrap(host, token);
        var seeded = UserAnalyticsProcessMutations.Write(host, update: false, token);
        var cut = UserAnalyticsProcessState.Capture(host, seeded.Operation, seeded.Receipt);
        await UserAnalyticsProcessState.WriteAsync(root, cut, token);
        using var admission = host.Database.AdmitQuery(token);
        var snapshot = host.Database.CaptureAnalyticsAggregate(PartitionStoreProtocol.AdministratorId, P.Query(), token);
        var directory = UserAnalyticsGenerationPaths.Create(host, snapshot);
        var failures = new List<Exception>();
        try
        {
            using var generation = new AnalyticsStoreNativeGeneration(snapshot, directory);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                UserAnalyticsProcessState.Equal(P.Expected(updated: false), generation.Query(token));
                UserAnalyticsProcessState.Require(host, cut);
                if (generation.HasJoinedEngine || !Directory.Exists(directory) || !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    throw new InvalidOperationException(P.Invalid);
                }
                await CrashHostPause.WaitForKillAsync();
                GC.KeepAlive(generation);
            }, failures);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RecoverAsync(string root, PartitionHost host, ServerRuntimeOptions options, CancellationToken token)
    {
        var cut = await UserAnalyticsProcessState.ReadAsync(root, token);
        UserAnalyticsProcessState.Require(host, cut);
        UserAnalyticsRuntime? runtime = null;
        var failures = new List<Exception>();
        try
        {
            await using var ownedRuntime = new UserAnalyticsRuntime(options.Core.DatabaseLimits);
            runtime = ownedRuntime;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                ownedRuntime.Open(host);
                await using var work = await UserAnalyticsProcessBoundWork.OpenAsync(host, options.GrainRouting, token);
                RequireEmpty(host, ownedRuntime);
                token.ThrowIfCancellationRequested();
                var replay = host.Database.Apply(cut.Operation).Get<CommitReceipt>();
                UserAnalyticsProcessState.Equal(cut.Receipt, replay);
                UserAnalyticsProcessMutations.RequirePrivateEmpty(replay);
                UserAnalyticsProcessState.Require(host, cut);
                await AggregateAsync(host, ownedRuntime, work, cut, updated: false, token);
                _ = UserAnalyticsProcessMutations.Write(host, update: true, token);
                await AggregateAsync(host, ownedRuntime, work, cut, updated: true, token);
            }, failures);
        }
        catch (Exception error) when (NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        catch (Exception error) when (!NativeCqrsBoundaryErrors.IsNonFatal(error))
        {
            failures.Add(error);
        }
        if (runtime is not null)
        {
            ServerFailureObserver.Observe(() => RequireEmpty(host, runtime), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task AggregateAsync(PartitionHost host, UserAnalyticsRuntime runtime, UserAnalyticsProcessBoundWork work, UserAnalyticsProcessOriginal cut, bool updated, CancellationToken token)
    {
        var marker = UserAnalyticsProcessState.Capture(host, cut.Operation, cut.Receipt);
        await work.AggregateAsync(host, runtime, P.Query(), result =>
        {
            UserAnalyticsProcessState.Equal(P.Expected(updated), result);
            return Task.CompletedTask;
        }, token);
        UserAnalyticsProcessState.Require(host, marker);
        RequireEmpty(host, runtime);
    }

    private static void RequireEmpty(PartitionHost host, UserAnalyticsRuntime runtime)
    {
        var directory = Path.Combine(host.DirectoryPath, UserAnalyticsGenerationPaths.DirectoryName);
        if (!runtime.CanReleasePhysical || Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
        {
            throw new InvalidOperationException(P.Invalid);
        }
    }
}
