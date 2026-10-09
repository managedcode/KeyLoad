using System.Collections.Immutable;

namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Original checksum-bound progress is admitted only after genuine retained native slot re-observation.</summary>
internal static class ClusterRestoreProgressOwner
{
    internal const string ProgressName = "progress.native";
    private const string PendingSuffix = ".pending";
    private const long InitialRevision = 0;
    private const long RevisionStep = 1;
    private const string MissingProgress = "The original native restore progress authority is missing.";
    private const string Invalid = "The retained restore progress differs from its genuine native slots.";

    internal static ClusterRestoreProgress Read(ClusterRestoreOperationRuntime runtime, string operationRoot,
        string nodesRoot)
    {
        var path = Path.Combine(operationRoot, ProgressName);
        if (File.Exists(path + PendingSuffix))
        {
            return ClusterRestoreStateFile.RecoverPending<ClusterRestoreProgress>(path, runtime.Storage.Value,
            value => Require(runtime, value, nodesRoot)).Value;
        }
        if (File.Exists(path))
        {
            var original = ClusterRestoreStateFile.Read<ClusterRestoreProgress>(path, runtime.Storage.Value).Value;
            Require(runtime, original, nodesRoot);
            return original;
        }
        if (Directory.Exists(nodesRoot) || File.Exists(nodesRoot))
        { throw Errors.Fail(ErrorCode.RecoveryRequired, MissingProgress); }
        var initial = new ClusterRestoreProgress(ClusterRestoreProgress.CurrentVersion,
            runtime.Plan.OperationId, runtime.PlanDigest, InitialRevision, [], ClusterRestorePublicationState.Admitted, null);
        _ = ClusterRestoreStateFile.Write(path, initial, runtime.Storage.Value, replace: false);
        return initial;
    }

    internal static void Require(ClusterRestoreOperationRuntime runtime, ClusterRestoreProgress original,
        string nodesRoot)
    {
        if (original.Version != ClusterRestoreProgress.CurrentVersion || original.OperationId != runtime.Plan.OperationId
            || original.PlanDigest != runtime.PlanDigest || original.Revision < InitialRevision
            || original.SlotObservations.IsDefault || original.SlotObservations.Length > runtime.Plan.Slots.Length
            || !Enum.IsDefined(original.PublicationState))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
        foreach (var pair in original.SlotObservations.Zip(runtime.Plan.Slots))
        { ClusterRestoreNativeSlots.RequireSame(pair.First, ClusterRestoreNativeSlots.RequireExisting(runtime, pair.Second, nodesRoot)); }
        if (original.PublicationState != ClusterRestorePublicationState.Admitted
            && original.SlotObservations.Length != runtime.Plan.Slots.Length
            || original.PublicationState == ClusterRestorePublicationState.Published && original.TerminalReceipt is null
            || original.PublicationState != ClusterRestorePublicationState.Published && original.TerminalReceipt is not null)
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
    }

    internal static ClusterRestoreProgress Save(ClusterRestoreOperationRuntime runtime, string operationRoot,
        ClusterRestoreProgress previous, ImmutableArray<NativeCatalogRestoreSlotCompletion> slots,
        ClusterRestorePublicationState state, ClusterRestoreExecutionReceipt? receipt)
    {
        var next = previous with
        {
            Revision = checked(previous.Revision + RevisionStep),
            SlotObservations = slots,
            PublicationState = state,
            TerminalReceipt = receipt
        };
        _ = ClusterRestoreStateFile.Write(Path.Combine(operationRoot, ProgressName), next, runtime.Storage.Value, replace: true);
        return next;
    }
}
