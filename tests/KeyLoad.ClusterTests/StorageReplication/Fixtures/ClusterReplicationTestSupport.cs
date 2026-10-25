using System.Globalization;
using System.Text;
using ManagedCode.Communication;

namespace KeyLoad.ClusterTests.StorageReplication;

internal static class ClusterReplicationTestSupport
{
    internal const string ClusterDiagnosticsFailureKey = "KeyLoad.ClusterDiagnosticsFailure";
    internal const string ClusterReadinessFailureKey = "KeyLoad.ClusterReadinessFailure";
    internal const string ClusterRestartFailureKeyPrefix = "KeyLoad.ClusterRestartFailure.";
    private static readonly CompositeFormat NodeNameFormat = CompositeFormat.Parse("node{0}");

    internal static bool IsNonFatalCleanupFailure(Exception error) =>
        error is not OutOfMemoryException and not StackOverflowException and not AccessViolationException;

    internal static string NodeName(int number) => string.Format(CultureInfo.InvariantCulture, NodeNameFormat, number);

    internal static T Success<T>(Result<T> result)
    {
        if (result.IsFailed)
        {
            Assert.Fail($"{result.Problem.ErrorCode ?? "MissingProblem"}: {result.Problem.Detail ?? "The client operation failed."}");
        }

        return result.Value!;
    }

    internal static Result Success(Result result)
    {
        if (result.IsFailed)
        {
            Assert.Fail($"{result.Problem.ErrorCode ?? "MissingProblem"}: {result.Problem.Detail ?? "The client operation failed."}");
        }
        return result;
    }

    internal static async Task MissingCopiesAsync(Result<CommitReceipt> result)
    {
        await Assert.That(result.IsFailed).IsTrue();
        await Assert.That(result.Value).IsNull();
        await Assert.That(result.Problem?.ErrorCode is nameof(ErrorCode.OwnershipLost)
            or nameof(ErrorCode.UnknownWriteOutcome)).IsTrue();
    }

    internal static async Task<Result> RetryDuringSiloRecoveryAsync(Func<Task<Result>> action,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await action();
            if (result.IsSuccess)
            {
                return result;
            }
            var code = result.Problem.ErrorCode ?? "MissingProblem";
            await Assert.That(new[] { nameof(ErrorCode.UnknownWriteOutcome), nameof(ErrorCode.OwnershipLost) }).Contains(code)
                .Because($"Unexpected silo recovery retry result: {code}; {result.Problem.Detail ?? "No safe detail."}");
            await Task.Delay(TimeSpan.FromMilliseconds(250), TimeProvider.System, cancellationToken);
        }
    }

    internal static async Task EventuallyAsync(Func<Task<bool>> predicate, CancellationToken cancellationToken)
    {
        while (!await predicate())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(TimeSpan.FromMilliseconds(250), TimeProvider.System, cancellationToken);
        }
    }

    internal static async Task<Result<T>> RetryDuringSiloRecoveryAsync<T>(Func<Task<Result<T>>> action,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            var result = await action();
            if (result.IsSuccess)
            {
                return result;
            }

            var code = result.Problem.ErrorCode ?? "MissingProblem";
            await Assert.That(new[] { nameof(ErrorCode.UnknownWriteOutcome), nameof(ErrorCode.OwnershipLost) }).Contains(code)
                .Because($"Unexpected silo recovery retry result: {code}; {result.Problem.Detail ?? "No safe detail."}");
            await Task.Delay(TimeSpan.FromMilliseconds(250), TimeProvider.System, cancellationToken);
        }
    }
}
