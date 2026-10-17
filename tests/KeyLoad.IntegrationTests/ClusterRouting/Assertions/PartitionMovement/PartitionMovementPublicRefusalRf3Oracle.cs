using KeyLoad.IntegrationTests.Features.ClientApi;
using ModelContextProtocol.Protocol;

namespace KeyLoad.IntegrationTests.Features.ClusterRouting;

internal static class PartitionMovementPublicRefusalRf3Oracle
{
    internal const string AdministrationDetail = "Cluster administration is required.";
    internal const string MovementConflictDetail = "The partition movement identity was reused with different content.";
    internal const string DocumentMinimumDetail = "The document session token belongs to another incarnation.";
    internal const string IndexMinimumDetail = "The index wait token belongs to another incarnation, atomic partition or placement.";
    internal const string FeedCursorDetail = "The change-feed cursor requires a fresh authorized snapshot.";
    internal const string SafeGenericDetail = "The database operation could not be completed.";
    internal const string SafePermissionDetail = "The authenticated principal is not permitted to perform this operation.";

    internal static async Task SdkAsync<TProblem>(TProblem actual, ErrorCode code, string ownedDetail)
    {
        var expected = Errors.Problem(code, ownedDetail);
        await Assert.That(JsonDefaults.Serialize(expected).AsSpan().SequenceEqual(JsonDefaults.Serialize(actual))).IsTrue();
    }

    internal static async Task McpAsync(CallToolResult reply, ErrorCode code, string safeDetail)
    {
        await McpCallerAssertions.ErrorAsync(reply, code, dispatched: true).ConfigureAwait(false);
        var expected = Errors.Problem(code, safeDetail);
        var problem = reply.StructuredContent!.Value.GetProperty(McpCallerProtocol.Error);
        await Assert.That(string.Equals(problem.GetProperty(McpCallerProtocol.ProblemType).GetString(),
            expected.Type, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(problem.GetProperty(McpCallerProtocol.ProblemTitle).GetString(),
            expected.Title, StringComparison.Ordinal)).IsTrue();
        await Assert.That(string.Equals(problem.GetProperty(McpCallerProtocol.ProblemDetail).GetString(),
            safeDetail, StringComparison.Ordinal)).IsTrue();
    }
}
