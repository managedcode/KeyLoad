using KeyLoad.Storage.ZoneTree;

namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Admits one original immutable plan after current native archive credential and complete field verification.</summary>
internal static class ClusterRestoreOriginalPlan
{
    private const string PlanName = "plan.native";
    private const string PendingSuffix = ".pending";
    private const string Invalid = "The original restore plan is absent, foreign or inconsistent.";

    internal static ClusterRestoreStateFile.ReadResult<ClusterRestorePlan>? Read(string operationRoot,
        ZoneTreeStorageExecutionOptions policy)
    {
        var path = Path.Combine(operationRoot, PlanName);
        if (File.Exists(path))
        {
            if (File.Exists(path + PendingSuffix))
            { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
            return ClusterRestoreStateFile.Read<ClusterRestorePlan>(path, policy);
        }
        if (File.Exists(path + PendingSuffix))
        { return ClusterRestoreStateFile.Read<ClusterRestorePlan>(path + PendingSuffix, policy); }
        if (Directory.Exists(operationRoot) && Directory.EnumerateFileSystemEntries(operationRoot).Any())
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        return null;
    }

    internal static ClusterRestoreStateFile.ReadResult<ClusterRestorePlan> Admit(string operationRoot,
        ClusterRestorePlan candidate, ZoneTreeStorageExecutionOptions policy,
        ClusterRestoreStateFile.ReadResult<ClusterRestorePlan>? original, Action<NativeClusterRestoreStage>? observer = null)
    {
        var path = Path.Combine(operationRoot, PlanName);
        if (original is not null)
        {
            ClusterRestorePlanComparison.Require(original.Value, candidate);
            if (File.Exists(path))
            { return original; }
            // A first plan write cannot have admitted ANY native slot before its own checked rename.
            if (Directory.EnumerateFileSystemEntries(operationRoot).Any(value => value != path + PendingSuffix))
            { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
            var recovered = ClusterRestoreStateFile.RecoverPending<ClusterRestorePlan>(path, policy,
                retained => ClusterRestorePlanComparison.Require(retained, candidate));
            observer?.Invoke(NativeClusterRestoreStage.PlanPublished);
            return recovered;
        }
        Directory.CreateDirectory(operationRoot);
        _ = ClusterRestoreStateFile.Write(path, candidate, policy, replace: false);
        var retained = ClusterRestoreStateFile.Read<ClusterRestorePlan>(path, policy);
        ClusterRestorePlanComparison.Require(retained.Value, candidate);
        observer?.Invoke(NativeClusterRestoreStage.PlanPublished);
        return retained;
    }
}
