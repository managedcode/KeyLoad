namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeRetainedSnapshotSelectionProgram
{
    internal const string Source = """
            const retainedSnapshotArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/ClusterTests/RetainedReplicaCatchesUpThroughNativeSnapshotAndKeepsCommandOutcomes", localArgs[2]];
            const retainedSnapshot = nativeSelection(retainedSnapshotArgs, {});
            let retainedSnapshotUnsupportedCount = 0;
            for (const badFilter of ['/*/*/ClusterTests/*',
              '/*/*/ClusterTests/RetainedReplicaCatchesUp*',
              '/*/*/ClusterTests/RetainedReplicaCatchesUpThroughNativeSnapshotAndKeepsCommandOutcomes|/*/*/OtherCase/*',
              '/*/*/ClusterTests/RetainedReplicaCatchesUpThroughNativeSnapshotAndKeepsCommandOutcomes ']) {
              try { nativeSelection([retainedSnapshotArgs[0], '--KeyLoadTests:Filter='+badFilter, retainedSnapshotArgs[2]], {}); }
              catch (failure) {
                if (failure.message !== 'Invalid native local CLUSTER image selection.') throw failure;
                retainedSnapshotUnsupportedCount++; continue;
              }
              throw new Error('Unsupported retained native snapshot CLUSTER filter was accepted.');
            }
            """;
}
