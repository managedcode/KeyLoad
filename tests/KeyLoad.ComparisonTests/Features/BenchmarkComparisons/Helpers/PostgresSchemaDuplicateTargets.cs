using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;
using Npgsql;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class PostgresSchemaDuplicateTargets
{
    internal static async Task VerifyAsync(string connectionString, CancellationToken cancellationToken)
    {
        await VerifySequentialAsync(connectionString, cancellationToken);
        await VerifyConcurrentAsync(connectionString, cancellationToken);
    }

    private static async Task VerifySequentialAsync(string connectionString, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("D");
        var dataset = new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(PostgresSchemaSupport.Options(2)));
        var winner = new PostgresTarget(connectionString, runId, "comparison-test-image", NativeExecutionPolicyFixture.Read(), NativeExecutionPolicyFixture.Lifecycle());
        var loser = new PostgresTarget(connectionString, runId, "comparison-test-image", NativeExecutionPolicyFixture.Read(), NativeExecutionPolicyFixture.Lifecycle());
        var loserDisposeStarted = false;
        try
        {
            await winner.InitializeAsync(dataset, cancellationToken);
            var failure = await CaptureDuplicateFailureAsync(loser, dataset, cancellationToken);
            await Assert.That(failure).IsTypeOf<PostgresException>();
            await Assert.That(((PostgresException)failure!).SqlState).IsEqualTo(PostgresErrorCodes.DuplicateSchema);
            loserDisposeStarted = true;
            await PostgresSchemaSupport.DisposeTargetAsync(loser);
            await VerifyWinnerDataAsync(winner, dataset.Documents[0], cancellationToken);
        }
        finally
        {
            try
            {
                if (!loserDisposeStarted)
                {
                    await PostgresSchemaSupport.DisposeTargetAsync(loser);
                }
            }
            finally
            {
                await PostgresSchemaSupport.DisposeTargetAsync(winner);
            }
        }
    }

    private static async Task VerifyConcurrentAsync(string connectionString, CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("D");
        var dataset = new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(PostgresSchemaSupport.Options(2)));
        var first = new PostgresTarget(connectionString, runId, "comparison-test-image", NativeExecutionPolicyFixture.Read(), NativeExecutionPolicyFixture.Lifecycle());
        var second = new PostgresTarget(connectionString, runId, "comparison-test-image", NativeExecutionPolicyFixture.Read(), NativeExecutionPolicyFixture.Lifecycle());
        var firstDisposeStarted = false;
        var secondDisposeStarted = false;
        try
        {
            var winnerIndex = await ConcurrentWinnerIndexAsync(first, second, dataset, cancellationToken);
            var winner = winnerIndex == 0 ? first : second;
            var loser = winnerIndex == 0 ? second : first;
            firstDisposeStarted = winnerIndex != 0;
            secondDisposeStarted = winnerIndex == 0;
            await PostgresSchemaSupport.DisposeTargetAsync(loser);
            await VerifyWinnerDataAsync(winner, dataset.Documents[0], cancellationToken);
        }
        finally
        {
            try
            {
                if (!firstDisposeStarted)
                {
                    await PostgresSchemaSupport.DisposeTargetAsync(first);
                }
            }
            finally
            {
                if (!secondDisposeStarted)
                {
                    await PostgresSchemaSupport.DisposeTargetAsync(second);
                }
            }
        }
    }

    private static async Task<int> ConcurrentWinnerIndexAsync(PostgresTarget first, PostgresTarget second,
        BenchmarkDataset dataset, CancellationToken cancellationToken)
    {
        var results = await Task.WhenAll(CaptureDuplicateFailureAsync(first, dataset, cancellationToken),
            CaptureDuplicateFailureAsync(second, dataset, cancellationToken));
        var winnerIndex = Array.FindIndex(results, result => result is null);
        if (winnerIndex < 0)
        {
            throw new InvalidOperationException("Concurrent PostgreSQL targets produced no successful owner.");
        }
        await Assert.That(results.Count(result => result is not null)).IsEqualTo(1);
        var failure = results[1 - winnerIndex];
        await Assert.That(failure).IsTypeOf<PostgresException>();
        await Assert.That(((PostgresException)failure!).SqlState).IsEqualTo(PostgresErrorCodes.DuplicateSchema);
        return winnerIndex;
    }

    private static async Task<PostgresException?> CaptureDuplicateFailureAsync(PostgresTarget target,
        BenchmarkDataset dataset, CancellationToken cancellationToken)
    {
        try
        {
            await target.InitializeAsync(dataset, cancellationToken);
            return null;
        }
        catch (PostgresException exception)
        {
            return exception;
        }
    }

    private static async Task VerifyWinnerDataAsync(PostgresTarget target, BenchmarkDocument expected,
        CancellationToken cancellationToken)
    {
        await using var session = await target.OpenSessionAsync(cancellationToken);
        await Assert.That(BenchmarkDataset.SameDocument(await session.ReadAsync(expected, cancellationToken), expected)).IsTrue();
    }
}
