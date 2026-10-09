using System.Collections.Immutable;

namespace KeyLoad.Cli.Features.BackupRestore;

internal static class ClusterRestoreResumeNodes
{
    private const string StageDirectory = "nodes";
    private const string Invalid = "The retained restore target is absent, partial or no longer at its original terminal cut.";

    internal static ClusterRestoreExecutionReceipt Run(ClusterRestoreOperationRuntime runtime, string operationRoot,
        long started)
    {
        var stage = Path.Combine(operationRoot, StageDirectory);
        var destination = runtime.Plan.DestinationPath;
        var published = Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any();
        if (published && Directory.Exists(stage))
        { throw Errors.Fail(ErrorCode.Conflict, Invalid); }
        var root = published ? destination : stage;
        var original = ClusterRestoreProgressOwner.Read(runtime, operationRoot, root);
        if (published)
        {
            if (original.PublicationState == ClusterRestorePublicationState.Admitted)
            { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
            var actual = ClusterRestoreNativeSlots.RequireAll(runtime, destination);
            RequireSame(original.SlotObservations, actual);
            if (original.TerminalReceipt is { } terminal)
            { RequireReceipt(runtime, terminal); return terminal; }
            return Terminal(runtime, operationRoot, original, started);
        }
        if (original.PublicationState == ClusterRestorePublicationState.Published)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        if (original.PublicationState == ClusterRestorePublicationState.Ready)
        {
            ClusterRestoreResumePublication.Publish(runtime, original, stage);
            return Terminal(runtime, operationRoot, original, started);
        }
        Directory.CreateDirectory(stage);
        var slots = ImmutableArray.CreateBuilder<NativeCatalogRestoreSlotCompletion>(runtime.Plan.Slots.Length);
        foreach (var slot in runtime.Plan.Slots)
        {
            runtime.Work.Check();
            Directory.CreateDirectory(ClusterRestorePathValidation.Relative(stage, slot.StageName));
            slots.Add(ClusterRestoreNativeSlots.Continue(runtime, slot, stage));
            if (slots.Count > original.SlotObservations.Length)
            {
                original = ClusterRestoreProgressOwner.Save(runtime, operationRoot, original, slots.ToImmutable(),
                    ClusterRestorePublicationState.Admitted, null);
            }
        }
        original = ClusterRestoreProgressOwner.Save(runtime, operationRoot, original, slots.ToImmutable(),
            ClusterRestorePublicationState.Ready, null);
        runtime.Observer?.Invoke(NativeClusterRestoreStage.ReadyPublished);
        ClusterRestoreResumePublication.Publish(runtime, original, stage);
        return Terminal(runtime, operationRoot, original, started);
    }

    private static ClusterRestoreExecutionReceipt Terminal(ClusterRestoreOperationRuntime runtime,
        string operationRoot, ClusterRestoreProgress original, long started)
    {
        var actual = ClusterRestoreNativeSlots.RequireAll(runtime, runtime.Plan.DestinationPath);
        RequireSame(original.SlotObservations, actual);
        var receipt = new ClusterRestoreExecutionReceipt(runtime.Plan.CaptureId, Nodes(runtime),
            runtime.Clock.GetElapsedTime(started));
        _ = ClusterRestoreProgressOwner.Save(runtime, operationRoot, original, actual,
            ClusterRestorePublicationState.Published, receipt);
        RequireReceipt(runtime, receipt);
        return receipt;
    }

    private static ImmutableArray<ClusterRestoreNodeReceipt> Nodes(ClusterRestoreOperationRuntime runtime)
        => runtime.Plan.Slots.Select(slot =>
        {
            var mapping = runtime.Plan.Mappings.Single(value => value.Source.PhysicalShardId == slot.SourceOwnerId);
            var configured = runtime.Configuration.Value.Nodes.Single(value => value.SourceOwnerId == slot.SourceOwnerId
                && value.RelativeDataDirectory == slot.StageName);
            if (!mapping.Target.VoterIds.Contains(configured.VoterId, StringComparer.Ordinal))
            { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
            return new ClusterRestoreNodeReceipt(slot.SourceOwnerId, slot.TargetOwnerId, configured.VoterId,
                slot.StageName, slot.TargetNodeId, slot.TargetIncarnation, DispatchPaused: true);
        }).ToImmutableArray();

    private static void RequireSame(ImmutableArray<NativeCatalogRestoreSlotCompletion> expected,
        ImmutableArray<NativeCatalogRestoreSlotCompletion> actual)
    {
        if (expected.Length != actual.Length)
        { throw Errors.Fail(ErrorCode.RecoveryRequired, Invalid); }
        foreach (var pair in expected.Zip(actual))
        { ClusterRestoreNativeSlots.RequireSame(pair.First, pair.Second); }
    }

    private static void RequireReceipt(ClusterRestoreOperationRuntime runtime, ClusterRestoreExecutionReceipt original)
    {
        if (original.CaptureId != runtime.Plan.CaptureId || !original.Nodes.SequenceEqual(Nodes(runtime)))
        { throw Errors.Fail(ErrorCode.Corruption, Invalid); }
    }
}
