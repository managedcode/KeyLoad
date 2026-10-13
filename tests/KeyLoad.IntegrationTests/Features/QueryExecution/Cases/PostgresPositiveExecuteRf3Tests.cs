namespace KeyLoad.IntegrationTests.Features.QueryExecution;

[global::TUnit.Core.NotInParallelAttribute]
internal sealed class PostgresPositiveExecuteRf3Tests
{
    [global::TUnit.Core.TestAttribute]
    [global::TUnit.Core.ArgumentsAttribute(PostgresRf3Protocol.ScalarDialect)]
    [global::TUnit.Core.ArgumentsAttribute(PostgresRf3Protocol.JoinDialect)]
    public Task PositiveExecuteLimitsRetainWholeNativeNpgsqlSdkMcpResultsAuthorityRefusalRepairCancellationAndTwoColdCuts(int dialect)
        => PostgresPositiveExecuteFlow.RunAsync(dialect);
}
