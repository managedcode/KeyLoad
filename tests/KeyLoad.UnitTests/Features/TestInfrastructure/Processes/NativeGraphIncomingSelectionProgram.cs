namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeGraphIncomingSelectionProgram
{
    internal const string Source = """
            const graphIncomingArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/GraphIncomingRf3Tests/PersistedSourceGrantHidesCrossRowsAndTargetGraphDenialIsExplicit", localArgs[2]];
            let graphIncomingUnsupportedCount = 0;
            for (const badFilter of ['/*/*/GraphIncomingRf3Tests/*',
              '/*/*/GraphIncomingRf3Tests/PersistedSourceGrant*',
              '/*/*/GraphIncomingRf3Tests/PersistedSourceGrantHidesCrossRowsAndTargetGraphDenialIsExplicit|/*/*/OtherCase/*', '/*/*/GraphIncomingRf3Tests/PersistedSourceGrantHidesCrossRowsAndTargetGraphDenialIsExplicit ']) {
              try { nativeSelection([graphIncomingArgs[0], '--KeyLoadTests:Filter='+badFilter, graphIncomingArgs[2]], {}); }
              catch (failure) {
                if (failure.message !== 'Invalid native local RF3 image selection.') throw failure;
                graphIncomingUnsupportedCount++; continue;
              }
              throw new Error('Unsupported incoming graph RF3 filter was accepted.');
            }
            const graphIncoming = nativeSelection(graphIncomingArgs, {});
            """;
}
