using KeyLoad.Clustering;
using KeyLoad.Core;
using KeyLoad.Core.Features.Authorization;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.RecoveryTests.Features.Search;

internal static class AnnCrashScenario
{
    private const int FirstVector = 0;
    private const float HealthyFirst = 11.25f;
    private const float HealthySecond = -2.5f;
    private const float HealthyThird = 0.75f;

    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary, string mode)
    {
        var database = mode == AnnCrashContract.Prepare ? CrashDatabase.Create(store) : Open(store);
        var failures = new List<Exception>();
        try
        {
            var commands = new AnnCrashCommandOwner(database);
            var actual = commands;
            await ServerFailureObserver.ObserveAsync(() => RunCoreAsync(directory, database, boundary, mode, actual), failures);
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunCoreAsync(string directory, DatabaseEngine database,
        CanonicalCrashBoundary boundary, string mode, AnnCrashCommandOwner commands)
    {
        if (mode == AnnCrashContract.Prepare)
        {
            await PrepareAsync(directory, database, commands);
            return;
        }
        var request = OrleansSerialization.Deserialize<AnnMaintenanceRequest>(await File.ReadAllBytesAsync(Path.Combine(directory, AnnCrashContract.RequestFile)))
            with
        {
            Mode = AnnMaintenanceMode.Restore
        };
        var intent = OrleansSerialization.Deserialize<CommitProjectionBatchRequest>(await File.ReadAllBytesAsync(Path.Combine(directory, AnnCrashContract.IntentFile)));
        if (mode == AnnCrashContract.Fault)
        {
            boundary.Position = checked(database.Store.Position + AnnCrashContract.Version);
            boundary.Armed = true;
            _ = AnnCrashOperations.Commit(commands, intent);
            throw new InvalidOperationException(AnnCrashContract.Invalid);
        }
        if (mode == AnnCrashContract.Recover)
        {
            await CompleteAsync(directory, AnnCrashContract.RecoveredFile, database, request, commands);
            var id = Guid.NewGuid();
            _ = AnnCrashOperations.Apply(commands, OperationKind.Batch,
                new CommandRequest(id, request.Consumer.Partition, [AnnCrashOperations.Vector(FirstVector, [HealthyFirst, HealthySecond, HealthyThird])]), id).Get<CommitReceipt>();
            await CompleteAsync(directory, AnnCrashContract.HealthyFile, database, request, commands);
            return;
        }
        if (mode == AnnCrashContract.Verify)
        {
            await CompleteAsync(directory, AnnCrashContract.VerifiedFile, database, request, commands);
            return;
        }
        throw new InvalidOperationException(AnnCrashContract.Invalid);
    }

    private static async Task PrepareAsync(string directory, DatabaseEngine database, AnnCrashCommandOwner commands)
    {
        var request = AnnCrashOperations.Seed(commands);
        await File.WriteAllBytesAsync(Path.Combine(directory, AnnCrashContract.RequestFile), OrleansSerialization.Serialize(request));
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new AnnCrashRuntime(directory, database);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var began = await AnnCrashState.PhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.Begin);
                var page = AnnCrashOperations.Read(database, request, began.Source!.ThroughSequence);
                if (!page.HasMore || page.Entries.Length != AnnCrashContract.PageLimit)
                {
                    throw new InvalidOperationException(AnnCrashContract.Invalid);
                }
                var intent = new CommitProjectionBatchRequest(Guid.NewGuid(), request.Consumer, page.Token, []);
                _ = await AnnCrashState.PhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.ApplyPage, page);
                var pending = await AnnCrashState.PhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.StagePage, intent: intent);
                if (!pending.Pending || pending.ThroughSequence != page.ThroughSequence)
                {
                    throw new InvalidOperationException(AnnCrashContract.Invalid);
                }
                await File.WriteAllBytesAsync(Path.Combine(directory, AnnCrashContract.IntentFile), OrleansSerialization.Serialize(intent));
                await RecoveryProcessPause.WaitForKillAsync();
            }, failures);
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(string directory, string file, DatabaseEngine database, AnnMaintenanceRequest request,
        AnnCrashCommandOwner commands)
    {
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new AnnCrashRuntime(directory, database);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var began = await AnnCrashState.PhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.Begin);
                var completed = await AnnCrashReplay.FinishAsync(database, actual, commands, request, began);
                await AnnCrashState.WriteAsync(directory, file, database, request, completed.Result, completed.Receipt, actual);
            }, failures);
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static DatabaseEngine Open(ZoneTreeStore store)
        => new(store, new AuthorizationPolicy(), CrashExecutionOptions.DatabaseLimits(), CrashExecutionOptions.DueWork(),
            CrashExecutionOptions.EventSource(), CrashExecutionOptions.Messaging(), CrashExecutionOptions.GraphExecution(),
            CrashExecutionOptions.ChangeFeedExecution(), CrashExecutionOptions.RuntimeClaimsExecution(), CrashExecutionOptions.TimeSeriesExecution());
}
