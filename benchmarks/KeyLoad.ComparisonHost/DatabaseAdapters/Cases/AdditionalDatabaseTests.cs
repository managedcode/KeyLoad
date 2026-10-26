using KeyLoad.Comparisons;
using Microsoft.Extensions.Configuration;

namespace KeyLoad.ComparisonHost.BenchmarkComparisons;

/// <summary>Exercises complete native operations and healthy continuation on the selected real Aspire database.</summary>
[NotInParallel]
internal sealed class AdditionalDatabaseTests
{
    private const string TargetSetting = "Benchmarks:Target";
    private const string MissingTarget = "AdditionalDatabaseTargetRequired";
    private const int EmptyCorpus = 0, Clients = 16, First = 0, Deleted = 1, Stable = 2, Single = 1, InitialCount = 3;

    [Test]
    public async Task SelectedDatabasePreservesBodiesCancellationAndHealthyContinuation()
    {
        using var configuration = new ConfigurationManager();
        configuration.AddEnvironmentVariables();
        var target = configuration[TargetSetting] ?? throw new InvalidOperationException(MissingTarget);
        await AdditionalDatabaseResources.RunAsync(target, VerifyAsync, TestContext.Current!.Execution.CancellationToken);
    }

    private static async Task VerifyAsync(IComparisonTarget target, CancellationToken token)
    {
        var documents = (IDocumentComparisonTarget)target;
        var corpus = new DocumentComparisonCorpus(EmptyCorpus, Clients);
        await documents.InitializeDocumentsAsync(new(corpus, false, InitialCount), token);
        await using var session = await documents.OpenDocumentSessionAsync(token);
        await using var independent = await documents.OpenDocumentSessionAsync(token);
        await Assert.That(session.ClientIdentity).IsNotEqualTo(independent.ClientIdentity);
        await Assert.That((await session.ReadAsync(corpus.CreateDocument(First), token)).Value).IsNull();
        await session.CreateDocumentsAsync([.. Enumerable.Range(First, InitialCount).Select(corpus.CreateDocument)], token);
        await AssertDocumentAsync(independent, corpus.CreateDocument(First), token);
        await session.ExecuteAsync(Scenario.DocumentUpdate, corpus.Replacement(First), token);
        await AssertDocumentAsync(independent, corpus.Replacement(First), token);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => session.ReadAsync(corpus.CreateDocument(Stable), cancelled.Token));
        await AssertDocumentAsync(session, corpus.CreateDocument(Stable), token);
        var queries = (IBenchmarkSessionQueries)target;
        if (target.Profile.Name == BenchmarkAdditionalTargets.InfluxDb)
        {
            await Assert.That(target.Supports(Scenario.DocumentDelete)).IsFalse();
            await Assert.ThrowsAsync<NotSupportedException>(() => queries.BuildSessionIndexesAsync(token));
        }
        else
        {
            await queries.BuildSessionIndexesAsync(token);
            await session.ExecuteAsync(Scenario.DocumentDelete, corpus.CreateDocument(Deleted), token);
            await Assert.That((await independent.ReadAsync(corpus.CreateDocument(Deleted), token)).Value).IsNull();
        }
        await queries.QuerySessionAsync(First, Stable, Single, token);
        await VerifyReadbackAsync(session, corpus, target.Supports(Scenario.DocumentDelete), token);
    }

    private static async Task AssertDocumentAsync(IDocumentComparisonSession session, BenchmarkDocument expected, CancellationToken token)
        => await Assert.That(BenchmarkDataset.SameDocument((await session.ReadAsync(expected, token)).Value, expected)).IsTrue();

    private static async Task VerifyReadbackAsync(IDocumentComparisonSession session, DocumentComparisonCorpus corpus,
        bool deleted, CancellationToken token)
    {
        var actual = new List<FoundDocument>();
        await foreach (var row in session.ReadDocumentsAsync(token))
        {
            actual.Add(row);
        }
        var expected = Enumerable.Range(First, InitialCount).Where(index => !deleted || index != Deleted)
            .Select(index => index == First ? corpus.Replacement(index) : corpus.CreateDocument(index))
            .OrderBy(document => document.Id, StringComparer.Ordinal).ToArray();
        await Assert.That(actual.Count).IsEqualTo(expected.Length);
        for (var index = First; index < expected.Length; index++)
        {
            await Assert.That(BenchmarkDataset.SameDocument(actual[index], expected[index])).IsTrue();
        }
    }
}
