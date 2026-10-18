using System.Text.Json;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeCurrentModelSelectionAssertions
{
    private const string Selections = "currentModels";
    private const int ExpectedSelections = 10;
    private static readonly string[] Filters =
    [
        "/*/*/GraphSearchPathPolicyClusterTests/*",
        "/*/*/(SqlParserAdmissionClusterWholeFlowTests|GraphSearchPathPolicyClusterTests)/*",
        "/*/*/(SqlParserAdmissionClusterWholeFlowTests|GraphSearchPathPolicyClusterTests|SagaDueClusterTests|SagaTimeoutClusterTests|DocumentSessionReadClusterTests|StrongReadSessionAbsenceClusterTests)/*",
        "/*/*/AspireContainerFaultCommandTests/WrongStaleAndForeignIdsRefuseWithoutEffectsBeforeHealthySdkMcpRestart",
        "/*/*/AggregateReplayClusterLeaderLossTests/AcEvent008AcceptedRestartCancellationResumesSameReplacementAndPreservesReplay",
        "/*/*/PartitionQueryPublicValidationTests/*",
        "/*/*/CrossTenantClusterWholeFlowTests/ForeignWritesScansAndIndexesAreDeniedAndAuthorizedReceiptRemainsStable",
        "/*/*/FunctionalDatabaseColdReopenTests/SingleInstanceColdReopensAllModelsAndReplaysOriginalPublicCommands",
        "/*/*/FunctionalDatabaseColdReopenTests/ThreeNodeColdReopensAllModelsAndReplaysOriginalPublicCommands",
        "/*/*/FunctionalFileOwnershipClusterTests/DistinctNativeFilesRetainAllModelsAcrossCompetingOwnersAndTwoColdReopens"
    ];

    internal static async Task AssertAsync(JsonElement root)
    {
        var selections = root.GetProperty(Selections).EnumerateArray().ToArray();
        await Assert.That(selections.Length).IsEqualTo(ExpectedSelections);
        foreach (var (selection, expected) in selections.Zip(Filters))
        {
            await NativeTUnitSelectionTests.AssertLocalSelectorAsync(selection, expected).ConfigureAwait(false);
        }
    }
}
