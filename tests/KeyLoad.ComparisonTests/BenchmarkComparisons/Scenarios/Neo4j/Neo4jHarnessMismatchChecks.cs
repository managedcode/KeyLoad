using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>Executes execution corruption and corrected readback directly, without an obsolete benchmark runner.</summary>
internal static class Neo4jHarnessMismatchChecks
{
    private const int Records = 16;
    private const int Clients = 16;

    internal static async Task VerifyAsync(Uri endpoint, string password, string image, CancellationToken cancellationToken)
    {
        var corpus = new DocumentComparisonCorpus(Records, Clients);
        var runId = Guid.NewGuid().ToString("D");
        await using var target = CreateTarget(endpoint, password, runId, image);
        await target.InitializeDocumentsAsync(new(corpus, true, Records + 1), cancellationToken);
        await using var observer = new Neo4jHarnessMismatchObserver(endpoint, password, runId, corpus, cancellationToken);
        await using var client = await target.OpenDocumentSessionAsync(cancellationToken);
        observer.CorruptBeforeRead();
        var mismatch = await ReadMismatchAsync(client, corpus.CreateDocument(0), cancellationToken);
        await Assert.That(mismatch.Message).IsEqualTo("BenchmarkSessionReadMismatch");
        await Assert.That(mismatch.Message.Contains(password, StringComparison.Ordinal)).IsFalse();
        await Assert.That(mismatch.Message.Contains(runId, StringComparison.Ordinal)).IsFalse();
        observer.RestoreBeforeWrite();
        await Assert.That(observer.CorruptedBeforePointRead && observer.RestoredBeforeDocumentWrite).IsTrue();
        await BenchmarkSessionDocumentOperation.ExecuteAsync(client, Scenario.PointRead, corpus.CreateDocument(0), cancellationToken);
        var created = corpus.CreateDocument(Records);
        await BenchmarkSessionDocumentOperation.ExecuteAsync(client, Scenario.DocumentWrite, created, cancellationToken);
        await BenchmarkSessionDocumentOperation.ExecuteAsync(client, Scenario.PointRead, created, cancellationToken);
        await BenchmarkSessionDocumentOperation.ExecuteAsync(client, Scenario.DocumentDelete, created, cancellationToken);
        var corrected = await DocumentComparisonOracle.VerifyAsync(client.ReadDocumentsAsync(cancellationToken), corpus.Documents, cancellationToken);
        await Assert.That(corrected.Records).IsEqualTo(Records);
        await Assert.That(corrected.ActualSha256).IsEqualTo(corrected.ExpectedSha256);
    }

    private static async Task<ComparisonFailureException> ReadMismatchAsync(IComparisonSession client,
        BenchmarkDocument document, CancellationToken cancellationToken)
    {
        try
        {
            await BenchmarkSessionDocumentOperation.ExecuteAsync(client, Scenario.PointRead, document, cancellationToken);
        }
        catch (ComparisonFailureException failure)
        {
            return failure;
        }
        throw new InvalidOperationException("The execution read accepted the corrupted body.");
    }

    private static Neo4jTarget CreateTarget(Uri endpoint, string password, string runId, string image)
    {
        HttpClient? client = null;
        try
        {
            client = Neo4jHarnessQueryClient.CreateClient(endpoint, password);
            var target = new Neo4jTarget(client, runId, image, ExecutionPolicyFixture.Lifecycle(), ExecutionPolicyFixture.Read());
            client = null;
            return target;
        }
        finally
        {
            client?.Dispose();
        }
    }
}
