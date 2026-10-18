using KeyLoad.CrashHost.Features.ClusterRouting;
using KeyLoad.Server;

namespace KeyLoad.CrashHost.Features.ResourceExecution;

internal static class D2InventoryCrashScenario
{
    private const int EmptyCommandLineLength = 0;
    private const string InvalidInventoryArgumentsMessage = "The D2 recovery child requires its exact mode, root and native cut.";
    private const string InventoryJournalBoundaryMissingMessage = "The requested original native journal boundary was not reached.";
    private const int ArgumentCount = 3;
    private const int ModeArgument = 0;
    private const int RootArgument = 1;
    private const int CutArgument = 2;

    internal static async Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length == EmptyCommandLineLength || args[ModeArgument] != D2InventoryCrashProtocol.Mode)
        {
            return false;
        }
        if (args.Length != ArgumentCount || string.IsNullOrWhiteSpace(args[RootArgument])
            || !Enum.TryParse<D2InventoryCrashCut>(args[CutArgument], out var cut) || !Enum.IsDefined(cut))
        {
            throw new ArgumentException(InvalidInventoryArgumentsMessage);
        }
        await RunAsync(args[RootArgument], cut).ConfigureAwait(false);
        return true;
    }

    private static async Task RunAsync(string root, D2InventoryCrashCut cut)
    {
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            var armed = false;
            void Observe(ReplicaCrashBoundary boundary)
            {
                var selected = cut == D2InventoryCrashCut.EntryAcknowledged
                    ? ReplicaCrashBoundary.EntryAcknowledged : ReplicaCrashBoundary.CommitAcknowledged;
                if (armed && cut != D2InventoryCrashCut.AppliedAcknowledged && boundary == selected)
                {
                    CrashHostPause.AtBoundary();
                }
            }
            using var source = new ControlledPartitionMovementNativeNode(
                Path.Combine(root, D2InventoryCrashProtocol.SourceDirectory), D2InventoryCrashCorpus.Source.Owner,
                replicaObserver: Observe);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                using var target = new ControlledPartitionMovementNativeNode(
                    Path.Combine(root, D2InventoryCrashProtocol.TargetDirectory), D2InventoryCrashCorpus.Target.Owner);
                await ServerFailureObserver.ObserveAsync(async () =>
                {
                    var evidence = D2InventoryCrashCorpus.Seed(source, target, CancellationToken.None);
                    await File.WriteAllBytesAsync(Path.Combine(root, D2InventoryCrashProtocol.EvidenceFile),
                        NativeSerialization.Serialize(evidence)).ConfigureAwait(false);
                    armed = true;
                    var receipt = source.Journal.Submit(evidence.Original, CancellationToken.None).Get<CommitReceipt>();
                    if (cut != D2InventoryCrashCut.AppliedAcknowledged)
                    {
                        throw new InvalidOperationException(InventoryJournalBoundaryMissingMessage);
                    }
                    await File.WriteAllBytesAsync(Path.Combine(root, D2InventoryCrashProtocol.ReceiptFile),
                        NativeSerialization.Serialize(receipt)).ConfigureAwait(false);
                    await CrashHostPause.WaitForKillAsync().ConfigureAwait(false);
                }, failures).ConfigureAwait(false);
            }, failures).ConfigureAwait(false);
        }, failures).ConfigureAwait(false);
        ServerFailureObserver.ThrowIfAny(failures);
    }
}
