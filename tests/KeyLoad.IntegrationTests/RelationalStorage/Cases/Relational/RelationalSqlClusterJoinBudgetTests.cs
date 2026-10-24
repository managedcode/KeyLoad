using KeyLoad.Query;

namespace KeyLoad.IntegrationTests.RelationalStorage;

/// <summary>Proves existing database query budgets through real Orleans SDK and official MCP callers.</summary>
[NotInParallel]
internal sealed class RelationalSqlClusterJoinBudgetTests
{
    private const int WorkLimit = 5;
    private const int ResultByteLimit = 4_096;
    private const long ReadByteLimit = 8_192;

    [Test]
    public Task JoinWorkBudgetReturnsSafeErrorsAndPreservesStateBeforeHealthyFollowUp()
        => RunAsync(JoinBudgetKind.Work, new DatabaseLimits { MaxScanRecords = WorkLimit });

    [Test]
    public Task JoinReadByteBudgetReturnsSafeErrorsAndPreservesStateBeforeHealthyFollowUp()
        => RunAsync(JoinBudgetKind.ReadBytes, new DatabaseLimits { MaxQueryReadBytes = ReadByteLimit });

    [Test]
    public Task JoinedPageByteBudgetReturnsSafeErrorsAndPreservesStateBeforeHealthyFollowUp()
        => RelationalSqlClusterJoinBudgetFlow.ExecuteAsync(JoinBudgetKind.ResultBytes, new DatabaseLimits(),
            new QueryExecutionOptions { MaximumResultBytes = ResultByteLimit });

    private static Task RunAsync(JoinBudgetKind kind, DatabaseLimits limits)
        => RelationalSqlClusterJoinBudgetFlow.ExecuteAsync(kind, limits);
}
