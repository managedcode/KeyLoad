using System.Text;
using KeyLoad.Comparisons;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

internal sealed class IsolatedMutationCorpusTests
{
    [Test]
    public async Task AcIso005MutationIdsAreUniqueAcrossOperationsWarmupsRepetitionsAndScenarios()
    {
        var options = ComparisonHarnessInputs.Small with { Operations = 32, Warmup = 4, Repetitions = 3 };
        var data = new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(options));
        var ids = data.Documents.Select(document => document.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var scenario in new[] { Scenario.DocumentWrite, Scenario.DocumentUpdate, Scenario.DocumentDelete })
        {
            for (var repetition = 0; repetition < options.Repetitions; repetition++)
            {
                for (var operation = 0; operation < options.Operations; operation++)
                {
                    await Assert.That(ids.Add(data.Input(scenario, repetition, operation, false).Id)).IsTrue();
                }
                for (var operation = 0; operation < options.Warmup; operation++)
                {
                    await Assert.That(ids.Add(data.Input(scenario, repetition, operation, true).Id)).IsTrue();
                }
            }
        }
    }

    [Test]
    public async Task AcIso005UpdateStartsFromDifferentExactLengthBodyAndDeleteStartsExisting()
    {
        var data = new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(ComparisonHarnessInputs.Small));
        var input = data.Input(Scenario.DocumentUpdate, 0, 0, false);
        var initial = BenchmarkDataset.InitialMutationState(Scenario.DocumentUpdate, input);
        await Assert.That(initial.Id).IsEqualTo(input.Id);
        await Assert.That(initial.Number).IsEqualTo(input.Number);
        await Assert.That(BenchmarkDataset.SameJson(initial.Json, input.Json)).IsFalse();
        await Assert.That(Encoding.UTF8.GetByteCount(initial.Json)).IsEqualTo(data.Options.PayloadBytes);
        await Assert.That(BenchmarkDataset.InitialMutationState(Scenario.DocumentDelete, input)).IsEqualTo(input);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => BenchmarkDataset.InitialMutationState(Scenario.PointRead, input));
    }

    [Test]
    public async Task AcIso005ExistingWriteInputsAndSharedHashRemainStableAcrossNativeCounts()
    {
        var data = new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(ComparisonHarnessInputs.Small));
        var expected = data.CreateDocument(data.Options.Documents + data.Options.Warmup);
        var actual = data.Input(Scenario.DocumentWrite, 0, 0, false);
        await Assert.That(BenchmarkDataset.SameDocument(new(actual.Id, actual.Json), expected)).IsTrue();
        await Assert.That(actual.Vector.SequenceEqual(expected.Vector)).IsTrue();
        var two = new BenchmarkDataset(Microsoft.Extensions.Options.Options.Create(data.Options with { Topology = ComparisonTopology.TwoNode }));
        await Assert.That(two.Sha256).IsEqualTo(data.Sha256);
    }
}
