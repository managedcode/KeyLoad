using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests;

internal sealed class StreamCorpusTests
{
    private static ComparisonOptions Options => new()
    {
        Documents = 16,
        Operations = 12,
        Warmup = 3,
        Repetitions = 3,
        Concurrency = 2,
        Dimensions = 8,
        TopK = 3,
        PayloadBytes = 128
    };

    [Test]
    public async Task StreamMutationsNeverReuseSeedWarmupOrEarlierRepetitionIds()
    {
        var dataset = new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(Options));
        var ids = dataset.Documents.Select(document => document.Id).ToHashSet(StringComparer.Ordinal);
        for (var repetition = 0; repetition < Options.Repetitions; repetition++)
        {
            foreach (var warmup in new[] { true, false })
            {
                var count = warmup ? Options.Warmup : Options.Operations;
                for (var operation = 0; operation < count; operation++)
                {
                    var input = dataset.Input(Scenario.StreamAppend, repetition, operation, warmup);
                    await Assert.That(ids.Add(input.Id)).IsTrue();
                }
            }
        }
        await Assert.That(ids.Count).IsEqualTo(Options.Documents + Options.Repetitions * (Options.Warmup + Options.Operations));
    }

    [Test]
    public async Task EventOracleChecksIdentityRevisionAndCompletePayload()
    {
        const string EmptyPayload = "{}";
        var dataset = new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(Options));
        var document = dataset.Documents[0];
        var expected = new FoundEvent(BenchmarkDataset.EventId(document), 1, document.Json);
        await Assert.That(BenchmarkDataset.SameEvent(expected, document)).IsTrue();
        await Assert.That(BenchmarkDataset.SameEvent(expected with { EventId = BenchmarkDataset.EventId(dataset.Documents[1]) }, document)).IsFalse();
        await Assert.That(BenchmarkDataset.SameEvent(expected with { Revision = 0 }, document)).IsFalse();
        await Assert.That(BenchmarkDataset.SameEvent(expected with { Json = EmptyPayload }, document)).IsFalse();
        await Assert.That(BenchmarkDataset.SameEvent(null, document)).IsFalse();
    }

    [Test]
    public async Task TopologyDoesNotChangeCorpusOrLogicalEventIdentity()
    {
        var single = new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(Options with { Topology = ComparisonTopology.Standalone }));
        var replicated = new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(Options with { Topology = ComparisonTopology.Replicated }));
        await Assert.That(single.Sha256).IsEqualTo(replicated.Sha256);
        await Assert.That(BenchmarkDataset.EventId(single.Documents[0])).IsEqualTo(BenchmarkDataset.EventId(replicated.Documents[0]));
        await Assert.That(BenchmarkDataset.EventId(single.Documents[0])).IsNotEqualTo(BenchmarkDataset.EventId(single.Documents[1]));
        var read = single.Input(Scenario.StreamRead, 2, 0, false);
        await Assert.That(single.Documents.Select(document => document.Id)).Contains(read.Id);
    }
}
