namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class PostgresSchemaRegression
{
    internal static async Task VerifyAsync(string connectionString, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        await PostgresSchemaPublicFlow.VerifyAsync(connectionString, cancellationToken);
        await PostgresSchemaContextReset.VerifyAsync(connectionString, cancellationToken);
        await PostgresSchemaOwnership.VerifyDuplicateTargetsAsync(connectionString, cancellationToken);
        await PostgresSchemaOwnership.VerifyConservativeCleanupAsync(connectionString, cancellationToken);
        await PostgresSchemaFailure.VerifyAtomicFailureAsync(connectionString, cancellationToken);
        await PostgresSchemaFailure.VerifyLockCancellationAsync(connectionString, cancellationToken);
    }
}
