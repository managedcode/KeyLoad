using System.Runtime.CompilerServices;
using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class ScaledCorpusReadbackTests
{
    [Test]
    public async Task IndependentOrderedReadbackRejectsMissingDuplicateExtraAndChangedPayload()
    {
        var corpus = new ScaledComparisonCorpus(ScaledComparisonProfileParser.Parse("scaled-100k-c16"));
        await ScaledCorpusReadbackVerifier.VerifyAsync(FirstRecords(corpus, 100_000), corpus, TestContext.Current!.Execution.CancellationToken);
        await Assert.ThrowsExactlyAsync<ComparisonFailureException>(() => ScaledCorpusReadbackVerifier.VerifyAsync(
            EmptyRecords(), corpus, TestContext.Current!.Execution.CancellationToken));
        await Assert.ThrowsExactlyAsync<ComparisonFailureException>(() => ScaledCorpusReadbackVerifier.VerifyAsync(
            DuplicatedFirst(corpus), corpus, TestContext.Current!.Execution.CancellationToken));
        await Assert.ThrowsExactlyAsync<ComparisonFailureException>(() => ScaledCorpusReadbackVerifier.VerifyAsync(
            ExtraRecord(corpus, TestContext.Current!.Execution.CancellationToken), corpus, TestContext.Current!.Execution.CancellationToken));
        await Assert.ThrowsExactlyAsync<ComparisonFailureException>(() => ScaledCorpusReadbackVerifier.VerifyAsync(
            ChangedFirst(corpus), corpus, TestContext.Current!.Execution.CancellationToken));

    }

    private static async IAsyncEnumerable<FoundDocument> FirstRecords(ScaledComparisonCorpus corpus, int count,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        for (var number = 0; number < count; number++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var document = corpus.CreateDocument(number);
            yield return new(document.Id, document.Json);
            await Task.Yield();
        }
    }

    [Test]
    public async Task CancellationDisposesTheOriginalReadbackEnumerator()
    {
        var corpus = new ScaledComparisonCorpus(ScaledComparisonProfileParser.Parse("scaled-100k-c16"));
        using var cancellation = new CancellationTokenSource();
        var evidence = new CancellationEvidence();
        var error = await Assert.ThrowsExactlyAsync<OperationCanceledException>(() => ScaledCorpusReadbackVerifier.VerifyAsync(
            evidence.Read(corpus, cancellation), corpus, cancellation.Token));
        await Assert.That(error!.CancellationToken).IsEqualTo(cancellation.Token);
        await Assert.That(evidence.Disposed).IsTrue();
    }

    private static async IAsyncEnumerable<FoundDocument> EmptyRecords()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<FoundDocument> ExtraRecord(ScaledComparisonCorpus corpus,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var record in FirstRecords(corpus, corpus.Settings.Documents, cancellationToken))
        {
            yield return record;
        }
        var first = corpus.CreateDocument(0);
        yield return new(first.Id, first.Json);
    }

    private sealed class CancellationEvidence
    {
        internal bool Disposed { get; private set; }

        internal async IAsyncEnumerable<FoundDocument> Read(ScaledComparisonCorpus corpus, CancellationTokenSource source,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            try
            {
                for (var number = 0; number < 100; number++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var document = corpus.CreateDocument(number);
                    yield return new(document.Id, document.Json);
                    if (number == 2)
                    {
                        await source.CancelAsync();
                    }

                    await Task.Yield();
                }
            }
            finally
            {
                Disposed = true;
            }
        }
    }

    private static async IAsyncEnumerable<FoundDocument> DuplicatedFirst(ScaledComparisonCorpus corpus,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var first = corpus.CreateDocument(0);
        yield return new(first.Id, first.Json);
        yield return new(first.Id, first.Json);
        await Task.CompletedTask;
    }

    private static async IAsyncEnumerable<FoundDocument> ChangedFirst(ScaledComparisonCorpus corpus,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var first = corpus.CreateDocument(0);
        yield return new(first.Id, "{\"id\":\"wrong\"}");
        await Task.CompletedTask;
    }

}
