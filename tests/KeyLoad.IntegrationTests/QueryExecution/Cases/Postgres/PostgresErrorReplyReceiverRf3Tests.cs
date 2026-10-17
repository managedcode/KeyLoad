namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[global::TUnit.Core.NotInParallelAttribute]
internal sealed class PostgresErrorReplyReceiverRf3Tests
{
    [global::TUnit.Core.TestAttribute]
    [global::TUnit.Core.ArgumentsAttribute(PostgresRf3Protocol.ScalarDialect)]
    [global::TUnit.Core.ArgumentsAttribute(PostgresRf3Protocol.JoinDialect)]
    public Task ActualReceiverAbortRetainsOriginalErrorAndJoinedFlushCleanupBeforePolicyRepairHealthyCallersAndTwoColdCuts(int dialect)
        => PostgresErrorReceiverFlow.RunAsync(dialect);
}
