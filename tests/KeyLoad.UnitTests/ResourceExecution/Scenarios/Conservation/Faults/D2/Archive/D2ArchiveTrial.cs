using KeyLoad.Core;
using KeyLoad.Server;
using KeyLoad.UnitTests.Features.ClusterRouting;

namespace KeyLoad.UnitTests.Features.ResourceExecution;

internal static class D2ArchiveTrial
{
    internal static async Task ExecuteAsync(bool unknownSource, CancellationToken token)
    {
        var root = Path.Combine(Path.GetTempPath(), D2ArchiveData.DirectoryPrefix + Guid.NewGuid().ToString(D2ArchiveData.GuidFormat));
        Directory.CreateDirectory(root);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(() => ControlledPartitionMovementTerminalOwners.ExecuteAsync(
            async (left, right, _, corpus) =>
            {
                var first = await D2ArchiveSeed.CreateAsync(left, corpus.Control, corpus.Destination,
                    D2ArchiveData.Empty, unknownSource, token);
                var second = await D2ArchiveSeed.CreateAsync(right, corpus.Destination, corpus.Control,
                    D2ArchiveData.Single, false, token);
                var captureId = Guid.NewGuid();
                D2ArchiveCapture[] archives =
                [
                    await D2ArchiveCaptureFlow.CaptureAsync(first, root, captureId, token),
                    await D2ArchiveCaptureFlow.CaptureAsync(second, root, captureId, token)
                ];
                await RestoreAsync(archives, root, token);
                foreach (var archive in archives)
                {
                    await D2ArchiveCaptureFlow.UnchangedAsync(archive, token);
                }
            }), failures);
        if (failures.Count == D2ArchiveData.Empty)
        {
            ServerFailureObserver.Observe(() => Directory.Delete(root, recursive: true), failures);
        }
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task RestoreAsync(D2ArchiveCapture[] archives, string root, CancellationToken token)
    {
        await Assert.That(archives.Length).IsEqualTo(D2ArchiveData.OwnerCount);
        var failures = new List<Exception>();
        await ServerFailureObserver.ObserveAsync(async () =>
        {
            using var plan = new D2ArchivePlan(archives, root);
            await ServerFailureObserver.ObserveAsync(async () =>
            {
                await D2ArchiveFaults.ExecuteAsync(plan, archives[D2ArchiveData.Empty], token);
                var work = new ReadExecutionBudget(UnitExecutionOptions.DatabaseLimits(), TimeProvider.System, token);
                var slots = archives.Select((archive, ordinal) => plan.Slot(archive, ordinal, "restored-" + ordinal)).ToArray();
                foreach (var slot in slots)
                {
                    var completed = D2ArchiveRestoreSlots.Restore(slot, work, token);
                    await CompletionAsync(slot, completed);
                    await D2ResourceInventoryAssertions.EqualAsync(completed, D2ArchiveRestoreSlots.Observe(slot, work, token));
                }
                foreach (var slot in slots)
                {
                    await D2ArchiveRestoredFlow.ExecuteAsync(slot, token);
                    await D2ArchiveCaptureFlow.UnchangedAsync(slot.Archive, token);
                }
            }, failures);
        }, failures);
        ServerFailureObserver.ThrowIfAny(failures);
    }

    private static async Task CompletionAsync(D2ArchiveSlot slot, NativeCatalogRestoreSlotCompletion actual)
    {
        var expected = new NativeCatalogRestoreSlotCompletion(NativeCatalogRestoreSlotCompletion.CurrentVersion,
            slot.Context, slot.Context.TargetNodeId, slot.Mapping.Target.Incarnation, slot.Context.TargetSignerFingerprint,
            checked(slot.Context.SourcePosition + D2ArchiveData.Single),
            checked(slot.Context.SourcePosition + D2ArchiveData.RestoreCommits),
            checked(slot.Context.SourcePosition + D2ArchiveData.RestoreCommits));
        await D2ResourceInventoryAssertions.EqualAsync(expected, actual);
    }
}
