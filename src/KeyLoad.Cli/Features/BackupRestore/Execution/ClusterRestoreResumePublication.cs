namespace KeyLoad.Cli.Features.BackupRestore;

/// <summary>Publishes only the complete genuine Ready cut under the original exclusive destination owner.</summary>
internal static class ClusterRestoreResumePublication
{
    private const string Invalid = "The original restore destination changed before checked native publication.";

    internal static void Publish(ClusterRestoreOperationRuntime runtime, ClusterRestoreProgress ready, string stage)
    {
        if (ready.PublicationState != ClusterRestorePublicationState.Ready)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        ClusterRestoreProgressOwner.Require(runtime, ready, stage);
        var destination = runtime.Plan.DestinationPath;
        ClusterRestorePathValidation.RequireAncestors(stage);
        ClusterRestorePathValidation.RequireAncestors(destination);
        if (!Directory.Exists(stage) || File.Exists(destination)
            || Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any())
        { throw Errors.Fail(ErrorCode.Conflict, Invalid); }
        runtime.Work.Check();
        // An interrupted deletion leaves an absent destination and the SAME admitted unpublished stage.
        if (Directory.Exists(destination))
        { Directory.Delete(destination, recursive: false); }
        Directory.Move(stage, destination);
        runtime.Observer?.Invoke(NativeClusterRestoreStage.NodesPublished);
        runtime.Work.Check();
        // Publication never substitutes historical progress for current native rows and complete model bytes.
        ClusterRestoreProgressOwner.Require(runtime, ready, destination);
    }
}
