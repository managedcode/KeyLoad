namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeResourcePolicySelectionProgram
{
    internal const string Source = """
        const resourcePolicyArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/ResourcePolicyUpdateClusterTests/AcRpol004PolicyAckFencesReadersCursorsAndStaleRetriedUpdatesAcrossCluster", localArgs[2]];
        const resourcePolicy = nativeSelection(resourcePolicyArgs, {});
        let resourcePolicyUnsupportedCount = 0;
        for (const badFilter of ['/*/*/ResourcePolicyUpdateClusterTests/*',
          '/*/*/ResourcePolicyUpdateClusterTests/AcRpol004*',
          '/*/*/ResourcePolicyUpdateClusterTests/AcRpol004PolicyAckFencesReadersCursorsAndStaleRetriedUpdatesAcrossCluster|/*/*/OtherCase/*',
          '/*/*/ResourcePolicyUpdateClusterTests/AcRpol004PolicyAckFencesReadersCursorsAndStaleRetriedUpdatesAcrossCluster ']) {
          try { nativeSelection([resourcePolicyArgs[0], '--KeyLoadTests:Filter='+badFilter, resourcePolicyArgs[2]], {}); }
          catch (failure) {
            if (failure.message !== 'Invalid native local CLUSTER image selection.') throw failure;
            resourcePolicyUnsupportedCount++; continue;
          }
          throw new Error('Unsupported policy epoch CLUSTER filter was accepted.');
        }
        """;
}
