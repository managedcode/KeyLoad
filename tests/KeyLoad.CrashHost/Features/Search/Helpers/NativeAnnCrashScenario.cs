using KeyLoad.Core;
using KeyLoad.Orleans;
using KeyLoad.Security;
using KeyLoad.Server;
using KeyLoad.Storage.ZoneTree;
using ManagedCode.Communication.CQRS;

namespace KeyLoad.CrashHost.Features.Search;

internal static class NativeAnnCrashScenario
{
    private const int FirstVector = 0;
    private const float HealthyFirst = 11.25f;
    private const float HealthySecond = -2.5f;
    private const float HealthyThird = 0.75f;

    internal static async Task RunAsync(string directory, ZoneTreeStore store, CanonicalCrashBoundary boundary, string mode)
    {
        var database = mode == NativeAnnCrashContract.Prepare ? CrashDatabase.Create(store) : Open(store);
        var failures = new List<Exception>();
        try
        {
            using var commands = new NativeAnnCrashCommandOwner(directory, database);
            var actual = commands;
            await ServerFailureObserver.ObserveAsync(() => RunCoreAsync(directory, database, boundary, mode, actual), failures);
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RunCoreAsync(string directory, DatabaseEngine database,
        CanonicalCrashBoundary boundary, string mode, NativeAnnCrashCommandOwner commands)
    {
        if (mode == NativeAnnCrashContract.Prepare)
        { await PrepareAsync(directory, database, commands); return; }
        var request = NativeSerialization.Deserialize<AnnMaintenanceRequest>(await File.ReadAllBytesAsync(Path.Combine(directory, NativeAnnCrashContract.RequestFile)))
            with
        { Mode = AnnMaintenanceMode.Restore };
        var intent = NativeSerialization.Deserialize<CommitProjectionBatchRequest>(await File.ReadAllBytesAsync(Path.Combine(directory, NativeAnnCrashContract.IntentFile)));
        if (mode == NativeAnnCrashContract.Fault)
        {
            boundary.Position = checked(database.Store.Position + NativeAnnCrashContract.Version);
            boundary.Armed = true;
            _ = NativeAnnCrashOperations.Commit(commands, intent);
            throw new InvalidOperationException(NativeAnnCrashContract.Invalid);
        }
        if (mode == NativeAnnCrashContract.Recover)
        {
            await CompleteAsync(directory, NativeAnnCrashContract.RecoveredFile, database, request, commands);
            var id = Guid.NewGuid();
            _ = NativeAnnCrashOperations.Apply(commands, OperationKind.Batch,
                new CommandRequest(id, request.Consumer.Partition, [NativeAnnCrashOperations.Vector(FirstVector, [HealthyFirst, HealthySecond, HealthyThird])]), id).Get<CommitReceipt>();
            await CompleteAsync(directory, NativeAnnCrashContract.HealthyFile, database, request, commands);
            return;
        }
        if (mode == NativeAnnCrashContract.Verify)
        { await CompleteAsync(directory, NativeAnnCrashContract.VerifiedFile, database, request, commands); return; }
        throw new InvalidOperationException(NativeAnnCrashContract.Invalid);
    }

    private static async Task PrepareAsync(string directory, DatabaseEngine database, NativeAnnCrashCommandOwner commands)
    {
        var request = NativeAnnCrashOperations.Seed(commands);
        await File.WriteAllBytesAsync(Path.Combine(directory, NativeAnnCrashContract.RequestFile), NativeSerialization.Serialize(request));
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new NativeAnnCrashRuntime(directory, database);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var began = await NativeAnnCrashState.PhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.Begin);
                var page = NativeAnnCrashOperations.Read(database, request, began.Source!.ThroughSequence);
                if (!page.HasMore || page.Entries.Length != NativeAnnCrashContract.PageLimit)
                { throw new InvalidOperationException(NativeAnnCrashContract.Invalid); }
                var intent = new CommitProjectionBatchRequest(Guid.NewGuid(), request.Consumer, page.Token, []);
                _ = await NativeAnnCrashState.PhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.ApplyPage, page);
                var pending = await NativeAnnCrashState.PhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.StagePage, intent: intent);
                if (!pending.Pending || pending.ThroughSequence != page.ThroughSequence)
                { throw new InvalidOperationException(NativeAnnCrashContract.Invalid); }
                await File.WriteAllBytesAsync(Path.Combine(directory, NativeAnnCrashContract.IntentFile), NativeSerialization.Serialize(intent));
                await CrashHostPause.WaitForKillAsync();
            }, failures);
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompleteAsync(string directory, string file, DatabaseEngine database, AnnMaintenanceRequest request,
        NativeAnnCrashCommandOwner commands)
    {
        var failures = new List<Exception>();
        try
        {
            await using var runtime = new NativeAnnCrashRuntime(directory, database);
            var actual = runtime;
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                var began = await NativeAnnCrashState.PhaseAsync(database, actual, request, AnnMaintenanceCapabilityKind.Begin);
                var completed = await NativeAnnCrashReplay.FinishAsync(database, actual, commands, request, began);
                await NativeAnnCrashState.WriteAsync(directory, file, database, request, completed.Result, completed.Receipt, actual);
            }, failures);
        }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is null) { failures.Add(error); }
        catch (Exception error) when (CqrsRuntimeFailures.FindFatal(error) is not null) { failures.Add(error); }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static DatabaseEngine Open(ZoneTreeStore store)
        => new(store, new AuthorizationPolicy(), CrashExecutionOptions.DatabaseLimits(), CrashExecutionOptions.DueWork(),
            CrashExecutionOptions.EventSource(), CrashExecutionOptions.Messaging(), CrashExecutionOptions.GraphExecution(),
            CrashExecutionOptions.ChangeFeedExecution(), CrashExecutionOptions.BlobExecution(),
            CrashExecutionOptions.NativeClaimsExecution(), CrashExecutionOptions.TimeSeriesExecution());
}
