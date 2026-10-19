using KeyLoad.Comparisons.Targets;
using Microsoft.Extensions.Options;
using Npgsql;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Owns the real target and cancellation until initialization and cleanup have settled.</summary>
internal static class PostgresSchemaLockOwner
{
    internal static async Task RunAsync(string connectionString, string runId, NpgsqlConnection blocker,
        NpgsqlTransaction transaction, long lockKey, IOptions<ComparisonHarnessOptions> harnessOptions,
        IsolatedTeardownFailures failures, CancellationToken cancellationToken)
    {
        var policy = harnessOptions.Value;
        await using var target = new PostgresTarget(connectionString, runId, "comparison-test-image",
            ExecutionPolicyFixture.Read(), ExecutionPolicyFixture.Lifecycle());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var initialization = PostgresSchemaFailure.CaptureCancellationAsync(target, cancellation.Token);
        try
        {
            await IsolatedOriginalTaskSettlement.RunAsync(() => AssertCancellationAsync(blocker, transaction,
                lockKey, cancellation, initialization, failures, harnessOptions, cancellationToken),
                "postgres-lock-assertion", failures, harnessOptions,
                () => ReleaseBlockerAsync(blocker, transaction, failures, harnessOptions));
        }
        finally
        {
            try
            {
                await SettleInitializationAsync(blocker, transaction, cancellation, initialization, failures, harnessOptions);
            }
            finally
            {
                await IsolatedOriginalTaskSettlement.RunAsync(() => PostgresSchemaOriginalTaskSettlement.JoinAsync(
                    target.DisposeAsync().AsTask(), policy.PostgresTargetDisposeTimeout, CancellationToken.None),
                    "postgres-target-dispose", failures, harnessOptions);
            }
        }
    }

    internal static async Task ReleaseBlockerAsync(NpgsqlConnection blocker, NpgsqlTransaction transaction,
        IsolatedTeardownFailures failures, IOptions<ComparisonHarnessOptions> harnessOptions)
    {
        await IsolatedOriginalTaskSettlement.RunAsync(() => PostgresSchemaOriginalTaskSettlement.JoinAsync(
            transaction.RollbackAsync(CancellationToken.None), harnessOptions.Value.PostgresRollbackTimeout, CancellationToken.None),
            "postgres-escalated-rollback", failures, harnessOptions, () => blocker.DisposeAsync().AsTask());
        await IsolatedOriginalTaskSettlement.RunAsync(() => blocker.DisposeAsync().AsTask(),
            "postgres-blocker-close", failures, harnessOptions);
    }

    private static async Task SettleInitializationAsync(NpgsqlConnection blocker, NpgsqlTransaction transaction,
        CancellationTokenSource cancellation, Task<OperationCanceledException?> initialization,
        IsolatedTeardownFailures failures, IOptions<ComparisonHarnessOptions> harnessOptions)
    {
        var policy = harnessOptions.Value;
        await IsolatedOriginalTaskSettlement.RunAsync(cancellation.CancelAsync,
            "postgres-cancellation-callbacks", failures, harnessOptions);
        await IsolatedOriginalTaskSettlement.RunAsync(() => PostgresSchemaOriginalTaskSettlement.JoinAsync(
            initialization, policy.PostgresCancellationSettlementTimeout,
            () => ReleaseBlockerAsync(blocker, transaction, failures, harnessOptions), CancellationToken.None),
            "postgres-initialization", failures, harnessOptions,
            () => ReleaseBlockerAsync(blocker, transaction, failures, harnessOptions));
        await IsolatedOriginalTaskSettlement.RunAsync(() => PostgresSchemaOriginalTaskSettlement.JoinAsync(
            transaction.RollbackAsync(CancellationToken.None), policy.PostgresRollbackTimeout, CancellationToken.None),
            "postgres-rollback", failures, harnessOptions, () => blocker.DisposeAsync().AsTask());
    }

    private static async Task AssertCancellationAsync(NpgsqlConnection blocker, NpgsqlTransaction transaction,
        long lockKey, CancellationTokenSource cancellation, Task<OperationCanceledException?> initialization,
        IsolatedTeardownFailures failures, IOptions<ComparisonHarnessOptions> harnessOptions,
        CancellationToken cancellationToken)
    {
        await PostgresSchemaFailure.WaitForAdvisoryLockWaitAsync(blocker, lockKey, cancellationToken);
        await cancellation.CancelAsync();
        var failure = await PostgresSchemaOriginalTaskSettlement.JoinAsync(initialization,
            harnessOptions.Value.PostgresCancellationSettlementTimeout,
            () => ReleaseBlockerAsync(blocker, transaction, failures, harnessOptions), cancellationToken);
        await Assert.That(failure is OperationCanceledException).IsTrue();
    }
}
