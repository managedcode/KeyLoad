using System.Collections.Immutable;

namespace KeyLoad.AppHost.Features.CodeQuality;

/// <summary>Closed original-node contributors; identities include the exact native instance.</summary>
internal static class NativeCoverageRf3ContributorCatalog
{
    internal const int Count = 11;
    internal const string Filter = "/*/*/(PartitionQueryPublicRf3Tests|McpDocumentCrudParityTests|RelationalSqlRf3JoinTests|RelationalSqlRf3JoinAuthorizationTests|RelationalSqlRf3JoinBudgetTests|RelationalSqlRf3JoinCancellationTests|RelationalSqlRf3JoinReadCutTests)/*";

    private const string PartitionQueryClass = "KeyLoad.IntegrationTests.Features.QueryExecution.PartitionQueryPublicRf3Tests";
    private const string DocumentCrudClass = "KeyLoad.IntegrationTests.Features.DocumentStorage.McpDocumentCrudParityTests";
    private const string RelationalJoinClass = "KeyLoad.IntegrationTests.Features.RelationalStorage.RelationalSqlRf3JoinTests";
    private const string RelationalJoinAuthorizationClass = "KeyLoad.IntegrationTests.Features.RelationalStorage.RelationalSqlRf3JoinAuthorizationTests";
    private const string RelationalJoinCancellationClass = "KeyLoad.IntegrationTests.Features.RelationalStorage.RelationalSqlRf3JoinCancellationTests";
    private const string RelationalJoinReadCutClass = "KeyLoad.IntegrationTests.Features.RelationalStorage.RelationalSqlRf3JoinReadCutTests";
    private const string RelationalJoinBudgetClass = "KeyLoad.IntegrationTests.Features.RelationalStorage.RelationalSqlRf3JoinBudgetTests";

    private const string PartitionQueryReferenceOrder = "AcPquery006SdkAndOfficialMcpReturnIndependentFullReferenceOrder";
    private const string SdkPatchMcpDelete = "AcDstore001SdkPatchAndMcpDeleteMatchTheMirroredCrudLifecycle";
    private const string McpPatchSdkDelete = "AcDstore001McpPatchAndSdkDeleteMatchTheMirroredCrudLifecycle";
    private const string StaleExplicitReplacement = "AcDstore001StaleExplicitReplacementIsRejectedWithoutChangingRevisionTwo";
    private const string RelationalJoinCommittedPairs = "Q2JoinReturnsTheSameCommittedPairsToSdkAndOfficialMcpAcrossRf3";
    private const string RelationalJoinAuthorizationDenials = "RightResourceAndJoinFieldUseDenialsHaveNoEffectsAndHealthyCallersStillRead";
    private const string RelationalJoinCallerCancellation = "CallerCancellationOnSdkAndOfficialMcpJoinPathsIsFollowedByHealthyReads";
    private const string RelationalJoinAtomicReadCut = "ConcurrentAtomicPairChangesNeverExposeAMixedRf3Generation";
    private const string RelationalJoinWorkBudget = "JoinWorkBudgetReturnsSafeErrorsAndPreservesStateBeforeHealthyFollowUp";
    private const string RelationalJoinReadByteBudget = "JoinReadByteBudgetReturnsSafeErrorsAndPreservesStateBeforeHealthyFollowUp";
    private const string RelationalJoinPageByteBudget = "JoinedPageByteBudgetReturnsSafeErrorsAndPreservesStateBeforeHealthyFollowUp";

    internal static ImmutableArray<NativeCoverageRf3Contributor> Contributors { get; } =
    [
        new(PartitionQueryClass, PartitionQueryReferenceOrder, PartitionQueryReferenceOrder),
        new(DocumentCrudClass, SdkPatchMcpDelete, SdkPatchMcpDelete),
        new(DocumentCrudClass, McpPatchSdkDelete, McpPatchSdkDelete),
        new(DocumentCrudClass, StaleExplicitReplacement, StaleExplicitReplacement),
        new(RelationalJoinClass, RelationalJoinCommittedPairs, RelationalJoinCommittedPairs),
        new(RelationalJoinAuthorizationClass, RelationalJoinAuthorizationDenials, RelationalJoinAuthorizationDenials),
        new(RelationalJoinCancellationClass, RelationalJoinCallerCancellation, RelationalJoinCallerCancellation),
        new(RelationalJoinReadCutClass, RelationalJoinAtomicReadCut, RelationalJoinAtomicReadCut),
        new(RelationalJoinBudgetClass, RelationalJoinWorkBudget, RelationalJoinWorkBudget),
        new(RelationalJoinBudgetClass, RelationalJoinReadByteBudget, RelationalJoinReadByteBudget),
        new(RelationalJoinBudgetClass, RelationalJoinPageByteBudget, RelationalJoinPageByteBudget)
    ];
}
