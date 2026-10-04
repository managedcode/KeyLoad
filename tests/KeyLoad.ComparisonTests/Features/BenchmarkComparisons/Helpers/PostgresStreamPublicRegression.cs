using KeyLoad.Comparisons;
using Npgsql;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class PostgresStreamPublicRegression
{
    private const int SeededDocumentIndex = 0;
    private const int NewStreamOffset = 20;
    private const int AbsentStreamOffset = 21;
    private const int FollowingStreamOffset = 22;
    private const ulong ExpectedRevision = 1;
    private const string ConflictingJson = "{\"payload\":\"conflict\"}";

    internal static async Task VerifyAsync(IComparisonSession session, BenchmarkDataset dataset,
        CancellationToken cancellationToken)
    {
        var seeded = dataset.Documents[SeededDocumentIndex];
        await AssertEventAsync(await session.ReadEventAsync(seeded, cancellationToken), seeded);

        var absent = dataset.CreateDocument(dataset.Options.Documents + AbsentStreamOffset);
        await Assert.That(await session.ReadEventAsync(absent, cancellationToken)).IsNull();

        var appended = dataset.CreateDocument(dataset.Options.Documents + NewStreamOffset);
        await session.ExecuteAsync(Scenario.StreamAppend, appended, cancellationToken);
        await AssertEventAsync(await session.ReadEventAsync(appended, cancellationToken), appended);

        var conflicting = appended with { Json = ConflictingJson };
        var duplicateFailure = await CapturePostgresFailureAsync(() => session.ExecuteAsync(
            Scenario.StreamAppend, conflicting, cancellationToken));
        await Assert.That(duplicateFailure).IsTypeOf<PostgresException>();
        await Assert.That(((PostgresException)duplicateFailure!).SqlState)
            .IsEqualTo(PostgresErrorCodes.UniqueViolation);
        await AssertEventAsync(await session.ReadEventAsync(appended, cancellationToken), appended);

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var cancellationFailure = await CaptureCancellationAsync(() => session.ReadEventAsync(seeded, cancelled.Token));
        await Assert.That(cancellationFailure is OperationCanceledException).IsTrue();

        var following = dataset.CreateDocument(dataset.Options.Documents + FollowingStreamOffset);
        await session.ExecuteAsync(Scenario.StreamAppend, following, cancellationToken);
        await AssertEventAsync(await session.ReadEventAsync(following, cancellationToken), following);
        await AssertEventAsync(await session.ReadEventAsync(seeded, cancellationToken), seeded);
    }

    private static async Task AssertEventAsync(FoundEvent? actual, BenchmarkDocument expected)
    {
        await Assert.That(actual is not null).IsTrue();
        await Assert.That(actual!.EventId).IsEqualTo(BenchmarkDataset.EventId(expected));
        await Assert.That(actual.Revision).IsEqualTo(ExpectedRevision);
        await Assert.That(BenchmarkDataset.SameEvent(actual, expected)).IsTrue();
    }

    private static async Task<PostgresException?> CapturePostgresFailureAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return null;
        }
        catch (PostgresException exception)
        {
            return exception;
        }
    }

    private static async Task<OperationCanceledException?> CaptureCancellationAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return null;
        }
        catch (OperationCanceledException exception)
        {
            return exception;
        }
    }
}
