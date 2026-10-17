using KeyLoad.Core;
using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Replication;
using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ResourceExecution;

internal static class D2ImportCrashScenario
{
    private const int EmptyCommandLineLength = 0;
    private const string InvalidImportArgumentsMessage = "The active-import child requires its exact root and native cut.";
    private const string ImportTargetCutChangedMessage = "The original active-import target cut changed before child admission.";
    private const string ImportJournalBoundaryMissingMessage = "The actual durable import-overlap boundary was not reached.";
    private const string ActiveImportFenceMissingMessage = "The genuine unfinished image import did not fence inventory.";
    private const int ArgumentCount = 3;
    private const int ModeIndex = 0;
    private const int RootIndex = 1;
    private const int CutIndex = 2;

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length == EmptyCommandLineLength || args[ModeIndex] != D2ImportCrashProtocol.Mode)
        {
            return false;
        }
        if (args.Length != ArgumentCount || !Enum.TryParse<D2InventoryCrashCut>(args[CutIndex], out var cut)
            || !Enum.IsDefined(cut))
        {
            throw new ArgumentException(InvalidImportArgumentsMessage);
        }
        await RunAsync(Path.GetFullPath(args[RootIndex]), cut);
        return true;
    }

    private static async Task RunAsync(string root, D2InventoryCrashCut cut)
    {
        var input = await ControlledPartitionMovementProcessFiles.ReadAsync<D2ImportCrashInput>(
            root, D2ImportCrashProtocol.InputFile, CancellationToken.None);
        var armed = false;
        void Observe(ReplicaCrashBoundary boundary)
        {
            var expected = cut == D2InventoryCrashCut.EntryAcknowledged
                ? ReplicaCrashBoundary.EntryAcknowledged : ReplicaCrashBoundary.CommitAcknowledged;
            if (armed && cut != D2InventoryCrashCut.AppliedAcknowledged && boundary == expected)
            {
                CrashHostPause.AtBoundary();
            }
        }
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var target = new ControlledPartitionMovementNativeNode(
                Path.Combine(root, D2ImportCrashProtocol.TargetDirectory), input.Owner,
                maximumFixtureEntries: ControlledPartitionMovementNativeJournal.TerminalHistoryEntries,
                replicaObserver: Observe);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                if (target.Store.Position != input.BeforePosition || target.Journal.Log.State.LastIndex != input.BeforeIndex)
                {
                    throw new InvalidOperationException(ImportTargetCutChangedMessage);
                }
                RequireBlocked(target);
                armed = true;
                var result = target.Journal.Submit(input.Original, CancellationToken.None);
                _ = result.Get<CommitReceipt>();
                if (cut != D2InventoryCrashCut.AppliedAcknowledged)
                {
                    throw new InvalidOperationException(ImportJournalBoundaryMissingMessage);
                }
                await ControlledPartitionMovementProcessFiles.WriteAsync(root, D2ImportCrashProtocol.ResultFile,
                    result, CancellationToken.None);
                await CrashHostPause.WaitForKillAsync();
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static void RequireBlocked(ControlledPartitionMovementNativeNode target)
    {
        var work = new ReadExecutionBudget(CrashExecutionOptions.DatabaseLimits(), target.Database.EvaluationClock);
        var request = target.Database.AdmitResourceOwnerInventoryRequest(D2ImportCrashProtocol.Principal,
            Guid.NewGuid(), D2ImportCrashProtocol.Tenant, D2ImportCrashProtocol.Database,
            D2ImportCrashProtocol.LocalCollection, D2ImportCrashProtocol.Single,
            target.Database.EvaluationClock.GetUtcNow().AddSeconds(target.Database.Limits.QueryDeadlineSeconds), work);
        var control = target.Database.CaptureResourceOwnerInventoryControl(D2ImportCrashProtocol.Principal, request, work);
        var observed = control.Directory.Owners.Single(owner => owner.Owner.PhysicalShardId == target.Database.ReadPhysicalShardCatalog(
            D2ImportCrashProtocol.Principal).DefaultShard.PhysicalShardId);
        try
        {
            _ = target.Database.ReadResourceOwnerInventoryPage(D2ImportCrashProtocol.Principal, control, observed, null, work);
        }
        catch (KeyLoadException failure) when (failure.Code == ErrorCode.RecoveryRequired)
        {
            return;
        }
        throw new InvalidOperationException(ActiveImportFenceMissingMessage);
    }
}
