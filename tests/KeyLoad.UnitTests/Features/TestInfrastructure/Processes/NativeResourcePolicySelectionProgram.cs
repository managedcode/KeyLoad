namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeResourcePolicySelectionProgram
{
    internal const string Source = """
        const resourcePolicyArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/ResourcePolicyUpdateRf3Tests/AcRpol004PolicyAckFencesReadersCursorsAndStaleRetriedUpdatesAcrossRf3", localArgs[2]];
        const resourcePolicy = nativeSelection(resourcePolicyArgs, {});
        let resourcePolicyUnsupportedCount = 0;
        for (const badFilter of ['/*/*/ResourcePolicyUpdateRf3Tests/*',
          '/*/*/ResourcePolicyUpdateRf3Tests/AcRpol004*',
          '/*/*/ResourcePolicyUpdateRf3Tests/AcRpol004PolicyAckFencesReadersCursorsAndStaleRetriedUpdatesAcrossRf3|/*/*/OtherCase/*',
          '/*/*/ResourcePolicyUpdateRf3Tests/AcRpol004PolicyAckFencesReadersCursorsAndStaleRetriedUpdatesAcrossRf3 ']) {
          try { nativeSelection([resourcePolicyArgs[0], '--KeyLoadTests:Filter='+badFilter, resourcePolicyArgs[2]], {}); }
          catch (failure) {
            if (failure.message !== 'Invalid native local RF3 image selection.') throw failure;
            resourcePolicyUnsupportedCount++; continue;
          }
          throw new Error('Unsupported policy epoch RF3 filter was accepted.');
        }
        """;
}
