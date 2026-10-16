using System.Text.Json;

namespace KeyLoad.UnitTests.Features.TestInfrastructure;

internal static class NativeCurrentModelSelectionAssertions
{
    private const string Selections = "currentModels";
    private const int ExpectedSelections = 23;
    private static readonly string[] Filters =
    [
        "/*/*/GraphSearchPathPolicyRf3Tests/*",
        "/*/*/SampleChunkCorrectionPredicateRf3Tests/*",
        "/*/*/(SqlParserAdmissionRf3WholeFlowTests|GraphSearchPathPolicyRf3Tests)/*",
        "/*/*/(SqlParserAdmissionRf3WholeFlowTests|GraphSearchPathPolicyRf3Tests|SampleChunkCorrectionPredicateRf3Tests)/*",
        "/*/*/(GraphSearchPathPolicyRf3Tests|SampleChunkCorrectionPredicateRf3Tests)/*",
        "/*/*/(NativeDatabaseMigrationRf3Tests|SqlParserAdmissionRf3WholeFlowTests|GraphSearchPathPolicyRf3Tests|RequestCqrsPhaseFaultTests|RequestCqrsAuthorityFaultTests|RequestCqrsRf3FollowerRestartTests|SagaDueRf3Tests|SagaTimeoutRf3Tests|SampleChunkCorrectionPredicateRf3Tests|ConnectionRf3AuthorizationTests|DocumentSessionReadRf3Tests|MetadataBootstrapRf3Tests|PartitionMovementPublicParentFlowTests|PhysicalOwnerRegistrationRf3Tests|ProtectedDocumentRf3Tests|QueryContextSessionRf3Tests|StrongReadSessionAbsenceRf3Tests)/*",
        "/*/*/AspireContainerFaultCommandTests/WrongStaleAndForeignIdsRefuseWithoutEffectsBeforeHealthySdkMcpRestart",
        "/*/*/AggregateReplayRf3LeaderLossTests/AcEvent008AcceptedRestartCancellationResumesSameReplacementAndPreservesReplay",
        "/*/*/ControlledPartitionMovementNativeRetireTests/OriginalNativeRetireCallerCancellationAndHostStopRetainAuthorityThenResumeReplayMixedModelsAndTwoColdOwners",
        "/*/*/PartitionQueryPublicValidationTests/*",
        "/*/*/CrossTenantRf3WholeFlowTests/Kl015ForeignWritesScansAndIndexesAreDeniedAndAuthorizedReceiptRemainsStable",
        "/*/*/RemotePartitionQueryRf3Tests/AcPqueryRemote001To005DestinationDenialLiteralMergeReceiptReplayAndOwnedRestart",
        "/*/*/FunctionalDatabaseColdReopenTests/SingleInstanceColdReopensAllModelsAndReplaysOriginalPublicCommands",
        "/*/*/FunctionalDatabaseColdReopenTests/ThreeNodeColdReopensAllModelsAndReplaysOriginalPublicCommands",
        "/*/*/(PhysicalOwnerRegistrationRf3Tests|RemotePartitionQueryRf3Tests|RequestCqrsPhaseFaultTests|RequestCqrsRf3FollowerRestartTests)/*",
        "/*/*/ControlledBlobLifetimeReadRf3Tests/HeldNativeRepliesRequireFreshSourceAuthorityAndSettleBeforeTwoColdBlobReadWaves",
        "/*/*/(PartitionMovementBlobWireNativeTests|NativeBlobWholeParentQualificationCases)/*",
        "/*/*/ResourcePartitionInventoryRf3Tests/AuthenticatedConservativeInventoryRetainsReplayRollbackAndTwoColdOwners",
        "/*/*/PartitionMovementPublicParentFlowTests/ActualPublicParentMovesAllLinkedModelsAndOriginalReceiptsAcrossTwoRf3OwnersAndBackAfterColdReplay",
        "/*/*/EmptyReplicaSnapshotRf3Tests/ErasedFollowerInstallsNativeSnapshotAndOrderedTailBeforeCompletePublicReplayAndHealthyContinuation",
        "/*/*/RequestCqrsAuthorityFaultTests/SdkWriteHeldAcrossPersistedRevocationIsUnauthorizedWithoutEffects",
        "/*/*/DropTableLifecycleRf3Tests/PublicDropRecreateRetainsAuthorityAndReplayAcrossTwoColdRf3Waves",
        "/*/*/FunctionalFileOwnershipRf3Tests/DistinctNativeFilesRetainAllModelsAcrossCompetingOwnersAndTwoColdReopens"
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
