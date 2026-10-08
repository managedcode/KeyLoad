using System.Globalization;
using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.CrashHost;

internal static class ReplicaPrefixGcCrashScenario
{
    private const int ArgumentCount = 5;
    private const int RootArgument = 0;
    private const int StageArgument = 1;
    private const int IndexArgument = 2;
    private const int ModeArgument = 3;
    private const int IncarnationArgument = 4;
    private const string InvalidArguments = "Invalid native replica-prefix GC crash arguments.";
    private const string MissingBoundary = "The actual native prefix reclamation boundary was not reached.";
    private const int PositionStep = 1;

    internal static Task<bool> TryRunAsync(string[] args)
    {
        if (args.Length <= ModeArgument || args[ModeArgument] != ReplicaPrefixGcCrashContract.Mode)
        { return Task.FromResult(false); }
        if (args.Length != ArgumentCount || !Enum.TryParse<CommitStage>(args[StageArgument], out var stage)
            || !Enum.IsDefined(stage) || !int.TryParse(args[IndexArgument], NumberStyles.None, CultureInfo.InvariantCulture, out var index)
            || index != ReplicaPrefixGcCrashContract.NoMutationIndex && (stage != CommitStage.MutationApplied || index != ReplicaPrefixGcCrashContract.LastPrefixMutationIndex)
            || !Guid.TryParse(args[IncarnationArgument], out var incarnation) || incarnation == Guid.Empty)
        { throw new ArgumentException(InvalidArguments, nameof(args)); }
        var boundary = new CanonicalCrashBoundary(stage, index, initiallyArmed: false);
        using var node = ReplicaCrashNode.OpenTarget(args[RootArgument], incarnation, replicaObserver: boundary.Observe);
        node.Populate(ReplicaPrefixGcCrashContract.SnapshotCut);
        _ = node.Snapshots.Create(ReplicaPrefixGcCrashContract.SnapshotCut, ReplicaPrefixGcCrashContract.InitialTerm);
        AppendTail(node);
        boundary.Position = checked(node.ReplicaStore.Position + PositionStep);
        boundary.Armed = true;
        _ = node.Snapshots.ReclaimCheckpointPrefix(CancellationToken.None);
        throw new InvalidOperationException(MissingBoundary);
    }

    internal static void AppendTail(ReplicaCrashNode node)
    {
        var operation = node.Database.NormalizeOperation(ReplicaCrashModel.Operation(ReplicaPrefixGcCrashContract.TailCut));
        node.Log.Append([new(ReplicaPrefixGcCrashContract.TailCut, ReplicaPrefixGcCrashContract.InitialTerm, operation)]);
        node.Log.Commit(ReplicaPrefixGcCrashContract.TailCut);
        _ = node.Database.Apply(operation, ReplicaPrefixGcCrashContract.TailCut).Get<CommitReceipt>();
    }
}
