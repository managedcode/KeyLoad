namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeGraphIncomingSelectionProgram
{
    internal const string Source = """
            const graphIncomingArgs = [localArgs[0], "--KeyLoadTests:Filter=/*/*/GraphIncomingClusterTests/PersistedSourceGrantHidesCrossRowsAndTargetGraphDenialIsExplicit", localArgs[2]];
            let graphIncomingUnsupportedCount = 0;
            for (const badFilter of ['/*/*/GraphIncomingClusterTests/*',
              '/*/*/GraphIncomingClusterTests/PersistedSourceGrant*',
              '/*/*/GraphIncomingClusterTests/PersistedSourceGrantHidesCrossRowsAndTargetGraphDenialIsExplicit|/*/*/OtherCase/*', '/*/*/GraphIncomingClusterTests/PersistedSourceGrantHidesCrossRowsAndTargetGraphDenialIsExplicit ']) {
              try { nativeSelection([graphIncomingArgs[0], '--KeyLoadTests:Filter='+badFilter, graphIncomingArgs[2]], {}); }
              catch (failure) {
                if (failure.message !== 'Invalid native local CLUSTER image selection.') throw failure;
                graphIncomingUnsupportedCount++; continue;
              }
              throw new Error('Unsupported incoming graph CLUSTER filter was accepted.');
            }
            const graphIncoming = nativeSelection(graphIncomingArgs, {});
            """;
}
