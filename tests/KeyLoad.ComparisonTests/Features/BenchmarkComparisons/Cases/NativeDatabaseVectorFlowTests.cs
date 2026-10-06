using KeyLoad.Comparisons;
using KeyLoad.Comparisons.Targets;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

[NotInParallel("NativeDatabaseFlows")]
internal sealed class NativeDatabaseVectorFlowTests
{
    [Test]
    [Arguments("SurrealDB", "exact")]
    [Arguments("SurrealDB", "hnsw")]
    [Arguments("HelixDB", "native")]
    public async Task NativeIndexFilterAndAcknowledgedUpdateReadActualVectors(string name, string method)
    {
        var token = TestContext.Current!.Execution.CancellationToken;
        await using var fixture = await NativeDatabaseFlowFixture.CreateAsync(name, token);
        using var client = await fixture.ClientAsync(token);
        var timeProvider = new NativeDatabaseFlowTimeProvider(TimeProvider.System);
        await using IVectorComparisonTarget target = name == "SurrealDB"
            ? new SurrealDbVectorTarget(client, fixture.Image, Guid.NewGuid().ToString(), NativeDatabaseFlowFixture.ExecutionOptions,
                NativeExecutionPolicyFixture.ReadSerialization(), timeProvider)
            : new HelixDbVectorTarget(client, fixture.Image, Guid.NewGuid().ToString(), NativeDatabaseFlowFixture.ExecutionOptions, timeProvider);
        var profile = VectorComparisonProfile.Parse("vector-100k-" + method + "-plain-c16");
        var corpus = new VectorComparisonCorpus(profile, NativeDatabaseFlowFixture.ExecutionOptions);
        await Assert.That(await target.IngestAsync(Seed(corpus, token), token)).IsEqualTo(256);
        var receipt = await target.BuildIndexAsync(profile, token);
        await Assert.That(receipt.IndexKind).IsEqualTo(profile.IndexKind);
        await Assert.That(receipt.BuildMilliseconds >= 0).IsTrue();
        var readback = new List<VectorReadback>();
        await foreach (var row in target.ReadbackAsync(token))
        {
            readback.Add(row);
        }

        await Assert.That(readback.Count).IsEqualTo(256);
        await Assert.That(readback.All(row => row.Dimensions == profile.Dimensions && row.VectorSha256 == VectorComparisonCorpus.HashVector(corpus.Create(row.Number).Embedding.Span))).IsTrue();
        await NativeDatabaseClockFlow.VerifyCancelledVectorRunnerAsync(target, profile,
            NativeDatabaseFlowFixture.ExecutionOptions, timeProvider, token);
        foreach (var mode in new[] { VectorQueryMode.Plain, VectorQueryMode.Filtered, VectorQueryMode.Mixed })
        {
            var actual = await target.SearchAsync(corpus.Create(0).Embedding, 2, mode, token);
            await Assert.That(actual.Count).IsEqualTo(2);
            if (method == "exact")
            {
                await Assert.That(actual.Select(row => row.Id).SequenceEqual(ExactIds(corpus, mode))).IsTrue();
            }
            await Assert.That(actual.All(row => Eligible(int.Parse(row.Id.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture), mode))).IsTrue();
            await Assert.That(actual.Select(row => row.Id).Distinct().Count()).IsEqualTo(2);
            await Assert.That(await target.ExplainAsync(corpus.Create(0).Embedding, mode, token)).IsNotEmpty();
        }
        var original = corpus.Create(19);
        var update = new VectorUpdate(19, original.Id, corpus.Create(31).Embedding);
        await target.UpdateAsync(update, token);
        await Assert.That((await target.ReadAsync(original.Id, token))!.VectorSha256).IsEqualTo(VectorComparisonCorpus.HashVector(update.Embedding.Span));
        var missing = corpus.Create(256);
        await Assert.That(await target.ReadAsync(missing.Id, token)).IsNull();
        await Assert.That(async () => await target.UpdateAsync(new VectorUpdate(missing.Number, missing.Id, original.Embedding), token)).Throws<ComparisonFailureException>();
        await Assert.That(await target.ReadAsync(missing.Id, token)).IsNull();
    }
    private static IEnumerable<string> ExactIds(VectorComparisonCorpus corpus, VectorQueryMode mode)
        => Enumerable.Range(0, 256).Where(number => Eligible(number, mode)).Select(number => corpus.Create(number))
            .OrderBy(document => CosineDistance(corpus.Create(0).Embedding.Span, document.Embedding.Span))
            .ThenBy(document => document.Id, StringComparer.Ordinal).Take(2).Select(document => document.Id);

    private static double CosineDistance(ReadOnlySpan<float> query, ReadOnlySpan<float> candidate)
    {
        double dot = 0, queryNorm = 0, candidateNorm = 0;
        for (var ordinal = 0; ordinal < query.Length; ordinal++)
        {
            dot += (double)query[ordinal] * candidate[ordinal];
            queryNorm += (double)query[ordinal] * query[ordinal];
            candidateNorm += (double)candidate[ordinal] * candidate[ordinal];
        }
        return 1 - dot / Math.Sqrt(queryNorm * candidateNorm);
    }

    private static bool Eligible(int number, VectorQueryMode mode)
        => mode == VectorQueryMode.Plain || mode == VectorQueryMode.Filtered && number % 100 == 0 || mode == VectorQueryMode.Mixed && number % 10 != 9;
    private static async IAsyncEnumerable<VectorDocument> Seed(VectorComparisonCorpus corpus,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        for (var index = 0; index < 256; index++)
        {
            token.ThrowIfCancellationRequested();
            yield return corpus.Create(index);
        }
        await Task.CompletedTask;
    }
}
