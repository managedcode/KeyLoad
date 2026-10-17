using System.Collections.Immutable;

namespace KeyLoad.IntegrationTests.Features.ClientApi;

internal static class McpPlacementDiscoveryExpectations
{
    private static readonly ImmutableArray<string> PolicyFields =
    [
        "version", "tenantId", "databaseId", "revision", "enabled", "promotionBytes", "demotionBytes",
        "minimumFreeBytes", "minimumFreeBasisPoints", "maximumMemoryBasisPoints", "maximumCpuBasisPoints",
        "requireDistinctHosts", "ownerWeights"
    ];
    private static readonly ImmutableArray<string> IntentFields =
    [
        "version", "intentId", "principalId", "policyEpoch", "plan", "revision", "completedMoves", "status",
        "receipts", "originalApplyCommandId", "claim", "controlOwner", "originalApplyRequest"
    ];
    internal static ImmutableArray<McpToolExpectation> Entries
    {
        get;
    } =
    [
        new("keyload_admin_placement_policy_configure", false, true, true, McpExpectedBody.Object, true,
            ["expectedRevision", "policy", "controlPartition"],
            PolicyFields,
            BodyPropertyFields: ["expectedRevision", "policy", "controlPartition"]),
        new("keyload_admin_placement_policy_read", true, true, false, McpExpectedBody.Object, false,
            ["tenantId", "databaseId", "controlPartition"],
            PolicyFields,
            BodyPropertyFields: ["tenantId", "databaseId", "controlPartition"]),
        new("keyload_admin_placement_plan", true, true, false, McpExpectedBody.Object, false,
            ["tenantId", "databaseId", "partitions", "controlPartition"],
            ["plan", "signedPlan"],
            BodyPropertyFields: ["tenantId", "databaseId", "partitions", "controlPartition"]),
        new("keyload_admin_placement_intent_persist", false, true, true, McpExpectedBody.Object, true,
            ["intentId", "expectedRevision", "signedPlan", "controlPartition"],
            IntentFields,
            BodyPropertyFields: ["intentId", "expectedRevision", "signedPlan", "controlPartition"]),
        new("keyload_admin_placement_intent_apply", false, true, true, McpExpectedBody.Object, true,
            ["tenantId", "databaseId", "intentId", "expectedRevision", "controlPartition"],
            IntentFields,
            BodyPropertyFields: ["tenantId", "databaseId", "intentId", "expectedRevision", "controlPartition"]),
        new("keyload_admin_placement_intent_read", true, true, false, McpExpectedBody.Object, false,
            ["scope", "intentId"],
            IntentFields,
            BodyPropertyFields: ["scope", "intentId"])
    ];
}
