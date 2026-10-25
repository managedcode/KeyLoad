using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.RelationalStorage;

/// <summary>Proves the configured query result byte bound through real Orleans SDK and official MCP callers.</summary>
[NotInParallel]
internal sealed class RelationalSqlClusterJoinBudgetTests
{
    private const int ResultByteLimit = 4_096;

    [Test]
    public Task JoinedPageByteBudgetReturnsSafeErrorsAndPreservesStateBeforeHealthyFollowUp()
        => RelationalSqlClusterJoinBudgetFlow.ExecuteAsync(
            new QueryExecutionOptions { MaximumResultBytes = ResultByteLimit });
}
