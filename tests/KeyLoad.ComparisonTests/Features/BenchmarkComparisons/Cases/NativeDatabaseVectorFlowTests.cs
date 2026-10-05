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
        await using IVectorComparisonTarget target = name == "SurrealDB"
            ? new SurrealDbVectorTarget(await fixture.ClientAsync(token), fixture.Image, Guid.NewGuid().ToString(), fixture.ExecutionOptions)
            : new HelixDbVectorTarget(await fixture.ClientAsync(token), fixture.Image, Guid.NewGuid().ToString(), fixture.ExecutionOptions);
        var profile = VectorComparisonProfile.Parse("vector-100k-" + method + "-plain-c16");
        var corpus = new VectorComparisonCorpus(profile);
        await Assert.That(await target.IngestAsync(Seed(corpus, token), token)).IsEqualTo(256);
        var receipt = await target.BuildIndexAsync(profile, token);
        await Assert.That(receipt.IndexKind).IsEqualTo(profile.IndexKind);
        await Assert.That(receipt.BuildMilliseconds >= 0).IsTrue();
        var readback = new List<VectorReadback>();
        await foreach (var row in target.ReadbackAsync(token)) readback.Add(row);
        await Assert.That(readback.Count).IsEqualTo(256);
        await Assert.That(readback.All(row => row.Dimensions == profile.Dimensions && row.VectorSha256 == VectorComparisonCorpus.HashVector(corpus.Document(row.Number).Embedding.Span))).IsTrue();
        foreach (var mode in new[] { VectorQueryMode.Plain, VectorQueryMode.Filtered, VectorQueryMode.Mixed })
        {
            var actual = await target.SearchAsync(corpus.Document(0).Embedding, 2, mode, token);
            await Assert.That(actual.Count).IsEqualTo(2);
            await Assert.That(actual.All(row => Eligible(int.Parse(row.Id.AsSpan(1), System.Globalization.CultureInfo.InvariantCulture), mode))).IsTrue();
            await Assert.That(actual.Select(row => row.Id).Distinct().Count()).IsEqualTo(2);
            await Assert.That(await target.ExplainAsync(corpus.Document(0).Embedding, mode, token)).IsNotEmpty();
        }
        var original = corpus.Document(19);
        var update = new VectorUpdate(19, original.Id, corpus.Document(31).Embedding);
        await target.UpdateAsync(update, token);
        await Assert.That((await target.ReadAsync(original.Id, token))!.VectorSha256).IsEqualTo(VectorComparisonCorpus.HashVector(update.Embedding.Span));
    }
    private static bool Eligible(int number, VectorQueryMode mode)
        => mode == VectorQueryMode.Plain || mode == VectorQueryMode.Filtered && number % 100 == 0 || mode == VectorQueryMode.Mixed && number % 10 != 9;
    private static async IAsyncEnumerable<VectorDocument> Seed(VectorComparisonCorpus corpus,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken token)
    {
        for (var index = 0; index < 256; index++)
        {
            token.ThrowIfCancellationRequested();
            yield return corpus.Document(index);
        }
        await Task.CompletedTask;
    }
}
