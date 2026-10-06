using Aspire.Hosting;
using Aspire.Hosting.Testing;
using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

internal static class KeyLoadStreamPublicRegression
{
    private const int SeededDocumentIndex = 0;
    private const int NewStreamOffset = 20;
    private const int AbsentStreamOffset = 21;
    private const int FollowingStreamOffset = 22;
    private const ulong ExpectedRevision = 1;
    private const string ConflictingJson = "{\"payload\":\"conflict\"}";
    private const string ConflictCode = "KeyLoad:RevisionConflict";
    private const string CancelledCode = "KeyLoad:Cancelled";

    internal static async Task VerifyAsync(DistributedApplication app, string adminKey,
        CancellationToken cancellationToken, int nodeCount = 3)
    {
        var clients = new HttpClient?[nodeCount];
        var ownershipTransferred = false;
        try
        {
            for (var index = 0; index < nodeCount; index++)
            {
                clients[index] = app.CreateHttpClient("node" + (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), "http");
            }
            var peers = clients.Select(client => client!).ToArray();
            await using var target = new KeyLoadTarget(peers[0], adminKey, Guid.NewGuid().ToString("N"),
                NativeExecutionPolicyFixture.Lifecycle(), NativeExecutionPolicyFixture.Read(), NativeExecutionPolicyFixture.ReadDiagnostics(), NativeExecutionPolicyFixture.Admission(),
                ComparisonClientOptions.Execution(), ComparisonClientOptions.Translation(), peers: peers, expectedNodes: nodeCount);
            ownershipTransferred = true;
            await VerifyTargetAsync(target, cancellationToken);
        }
        finally
        {
            if (!ownershipTransferred)
            {
                foreach (var client in clients)
                {
                    client?.Dispose();
                }
            }
        }
    }

    private static async Task VerifyTargetAsync(KeyLoadTarget target, CancellationToken cancellationToken)
    {
        var options = new ComparisonOptions
        {
            Documents = 4,
            Operations = 1,
            Warmup = 0,
            Repetitions = 1,
            Concurrency = 1,
            Dimensions = 8,
            TopK = 3,
            GraphVertices = 4,
            GraphFanOut = 1,
            GraphDepth = 1
        };
        var dataset = new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(options));
        await target.InitializeAsync(dataset, cancellationToken);
        await using var session = await target.OpenSessionAsync(cancellationToken);
        await AssertEventAsync(await session.ReadEventAsync(dataset.Documents[SeededDocumentIndex], cancellationToken),
            dataset.Documents[SeededDocumentIndex]);
        await VerifyAbsentAndAppendAsync(session, dataset, cancellationToken);
        await VerifyConflictAndRecoveryAsync(session, dataset, cancellationToken);
    }

    private static async Task VerifyAbsentAndAppendAsync(IComparisonSession session, BenchmarkDataset dataset,
        CancellationToken cancellationToken)
    {
        var absent = dataset.CreateDocument(dataset.Options.Documents + AbsentStreamOffset);
        await Assert.That(await session.ReadEventAsync(absent, cancellationToken)).IsNull();
        var appended = dataset.CreateDocument(dataset.Options.Documents + NewStreamOffset);
        await session.ExecuteAsync(Scenario.StreamAppend, appended, cancellationToken);
        await AssertEventAsync(await session.ReadEventAsync(appended, cancellationToken), appended);
    }

    private static async Task VerifyConflictAndRecoveryAsync(IComparisonSession session, BenchmarkDataset dataset,
        CancellationToken cancellationToken)
    {
        var appended = dataset.CreateDocument(dataset.Options.Documents + NewStreamOffset);
        var conflicting = appended with { Json = ConflictingJson };
        var duplicateFailure = await CaptureConflictAsync(() => session.ExecuteAsync(
            Scenario.StreamAppend, conflicting, cancellationToken));
        await Assert.That(duplicateFailure?.Message).IsEqualTo(ConflictCode);
        await AssertEventAsync(await session.ReadEventAsync(appended, cancellationToken), appended);
        await VerifyCancellationAsync(session, dataset, cancellationToken);
    }

    private static async Task VerifyCancellationAsync(IComparisonSession session, BenchmarkDataset dataset,
        CancellationToken cancellationToken)
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var cancellationFailure = await CaptureCancellationAsync(() => session.ReadEventAsync(
            dataset.Documents[SeededDocumentIndex], cancelled.Token), cancelled.Token);
        await Assert.That(cancellationFailure?.Message).IsEqualTo(CancelledCode);
        var following = dataset.CreateDocument(dataset.Options.Documents + FollowingStreamOffset);
        await session.ExecuteAsync(Scenario.StreamAppend, following, cancellationToken);
        await AssertEventAsync(await session.ReadEventAsync(following, cancellationToken), following);
        await AssertEventAsync(await session.ReadEventAsync(dataset.Documents[SeededDocumentIndex], cancellationToken),
            dataset.Documents[SeededDocumentIndex]);
    }

    private static async Task AssertEventAsync(FoundEvent? actual, BenchmarkDocument expected)
    {
        await Assert.That(actual is not null).IsTrue();
        await Assert.That(actual!.EventId).IsEqualTo(BenchmarkDataset.EventId(expected));
        await Assert.That(actual.Revision).IsEqualTo(ExpectedRevision);
        await Assert.That(BenchmarkDataset.SameEvent(actual, expected)).IsTrue();
    }

    private static async Task<ComparisonFailureException?> CaptureConflictAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return null;
        }
        catch (ComparisonFailureException exception)
        {
            return exception;
        }
    }

    private static async Task<ComparisonFailureException?> CaptureCancellationAsync(
        Func<Task> operation, CancellationToken cancellationToken)
    {
        try
        {
            await operation();
            return null;
        }
        catch (ComparisonFailureException exception) when (
            cancellationToken.IsCancellationRequested && exception.Message == CancelledCode)
        {
            return exception;
        }
    }
}
