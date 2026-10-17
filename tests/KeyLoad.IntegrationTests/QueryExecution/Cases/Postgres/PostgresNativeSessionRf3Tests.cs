namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[global::TUnit.Core.NotInParallelAttribute]
internal sealed class PostgresNativeSessionRf3Tests
{
    [global::TUnit.Core.TestAttribute]
    [global::TUnit.Core.ArgumentsAttribute(PostgresRf3Protocol.ScalarDialect)]
    [global::TUnit.Core.ArgumentsAttribute(PostgresRf3Protocol.JoinDialect)]
    public Task NativeTlsNpgsqlPreparedAndUnpreparedSqlRetainCurrentPolicyReceiptsCancellationAndTwoColdCuts(int dialect)
        => PostgresRf3Flow.RunAsync(dialect);
}
