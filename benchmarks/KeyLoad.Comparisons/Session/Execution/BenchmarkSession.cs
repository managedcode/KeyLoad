using Microsoft.Extensions.Options;

namespace KeyLoad.Comparisons;

/// <summary>Orders distinct measured tests over one fixture-owned native database without resetting its data.</summary>
public sealed class BenchmarkSession
{
    private readonly BenchmarkSessionDocumentWorkload documents;
    private readonly BenchmarkSessionQueryWorkload queries;
    private readonly BenchmarkSessionMixedWorkload mixed;
    private int active;

    /// <summary>Creates the frozen production session over the native target and its validated operational policy.</summary>
    public BenchmarkSession(IComparisonTarget target, IOptions<NativeComparisonExecutionOptions> executionOptions,
        BenchmarkSessionOptions? options = null, TimeProvider? provider = null, Action<DocumentComparisonProgress>? progress = null)
        : this(target, executionOptions, options ?? new BenchmarkSessionOptions(), false, provider, progress)
    {
    }

    internal BenchmarkSession(IComparisonTarget target, IOptions<NativeComparisonExecutionOptions> executionOptions,
        BenchmarkSessionOptions options, bool development, TimeProvider? provider = null, Action<DocumentComparisonProgress>? progress = null)
    {
        var context = new BenchmarkSessionContext(target, executionOptions, options, development, provider, progress);
        documents = new(context);
        queries = new(context);
        mixed = new(context);
    }

    /// <summary>Measures the initial retained million and verifies every actual record.</summary>
    public Task<BenchmarkSessionCaseReport> LoadAsync(CancellationToken cancellationToken) => ExecuteAsync(documents.LoadAsync, cancellationToken);
    /// <summary>Measures native post-load index construction where the selected engine exposes it.</summary>
    public Task<BenchmarkSessionCaseReport> IndexAsync(CancellationToken cancellationToken) => ExecuteAsync(queries.IndexAsync, cancellationToken);
    /// <summary>Measures ordered stable reads from the shared database.</summary>
    public Task<BenchmarkSessionCaseReport> SequentialReadAsync(CancellationToken cancellationToken)
        => ExecuteAsync(token => queries.ReadAsync(false, token), cancellationToken);
    /// <summary>Measures a deterministic stable-key permutation.</summary>
    public Task<BenchmarkSessionCaseReport> RandomReadAsync(CancellationToken cancellationToken)
        => ExecuteAsync(token => queries.ReadAsync(true, token), cancellationToken);
    /// <summary>Measures native bounded compound-predicate queries.</summary>
    public Task<BenchmarkSessionCaseReport> SearchAsync(CancellationToken cancellationToken) => ExecuteAsync(queries.SearchAsync, cancellationToken);
    /// <summary>Measures overlapping bounded mutation, read and native query streams.</summary>
    public Task<BenchmarkSessionCaseReport> MixedAsync(CancellationToken cancellationToken) => ExecuteAsync(mixed.RunAsync, cancellationToken);
    /// <summary>Verifies every retained key, complete value and required native replica copy.</summary>
    public Task<BenchmarkSessionCaseReport> VerifyAsync(CancellationToken cancellationToken) => ExecuteAsync(documents.VerifyAsync, cancellationToken);

    private async Task<BenchmarkSessionCaseReport> ExecuteAsync(Func<CancellationToken, Task<BenchmarkSessionCaseReport>> operation, CancellationToken token)
    {
        if (Interlocked.CompareExchange(ref active, BenchmarkSessionLimits.SingleItemCount, BenchmarkSessionLimits.NoObservedItems) != BenchmarkSessionLimits.NoObservedItems)
        {
            throw new InvalidOperationException(BenchmarkSessionProtocol.ConcurrentBenchmarkSessionTestsAreForbidden);
        }
        try
        {
            return await operation(token).ConfigureAwait(false);
        }
        finally
        {
            Volatile.Write(ref active, BenchmarkSessionLimits.NoObservedItems);
        }
    }
}
