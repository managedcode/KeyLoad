using KeyLoad.Comparisons;
using T = KeyLoad.ComparisonTests.Features.BenchmarkComparisons.IsolatedQuorumResourceTokens;

namespace KeyLoad.ComparisonTests.Features.BenchmarkComparisons;

/// <summary>AC-ISO-001/003: invalid cell labels fail before allocating native resources, credentials or files.</summary>
internal sealed class IsolatedQuorumResourceRejectionTests
{
    [Test]
    [Arguments(T.Qdrant, "zero")]
    [Arguments(T.Qdrant, "four")]
    [Arguments(T.Qdrant, "target")]
    [Arguments(T.Qdrant, "profile")]
    [Arguments(T.Qdrant, "scenario")]
    [Arguments(T.Rabbit, "zero")]
    [Arguments(T.Rabbit, "four")]
    [Arguments(T.Rabbit, "target")]
    [Arguments(T.Rabbit, "profile")]
    [Arguments(T.Rabbit, "scenario")]
    public async Task AcIso001SelectionMismatchDoesNotAllocateNativeResources(string helper, string corruption)
    {
        using var model = new IsolatedQuorumResourceModel();
        var selection = new ComparisonWorkerSelection(helper, 2, Scenario.PointRead, T.Profile);
        selection = corruption switch
        {
            "zero" => selection with { NodeCount = 0 },
            "four" => selection with { NodeCount = 4 },
            "target" => selection with { Target = helper == T.Qdrant ? T.Rabbit : T.Qdrant },
            "profile" => selection with { Profile = "unapproved" },
            "scenario" => selection with { Scenario = (Scenario)1000 },
            _ => throw new ArgumentOutOfRangeException(nameof(corruption))
        };
        Assert.ThrowsExactly<InvalidOperationException>(() => model.Add(helper, selection));
        await Assert.That(model.Builder.Resources.Select(resource => resource.Name)).IsEquivalentTo(new[] { T.Runner });
        await Assert.That(Directory.EnumerateFileSystemEntries(model.Root)).IsEmpty();
    }

    [Test]
    [Arguments(T.Qdrant)]
    [Arguments(T.Rabbit)]
    public async Task AcIso001IndependentCellsNeverReuseSecretsOrDataRoots(string engine)
    {
        using var one = new IsolatedQuorumResourceModel();
        using var two = new IsolatedQuorumResourceModel();
        one.Add(engine, 3);
        two.Add(engine, 3);
        one.BuildNodes();
        two.BuildNodes();
        var first = one.Builder.Resources.OfType<Aspire.Hosting.ApplicationModel.ParameterResource>().ToArray();
        var second = two.Builder.Resources.OfType<Aspire.Hosting.ApplicationModel.ParameterResource>().ToArray();
        await Assert.That(first.Length).IsEqualTo(engine == T.Qdrant ? 1 : 3);
        await Assert.That(second.Length).IsEqualTo(first.Length);
        await Assert.That(one.Root).IsNotEqualTo(two.Root);
        for (var index = 0; index < first.Length; index++)
        {
            var left = await first[index].GetValueAsync(TestContext.Current!.Execution.CancellationToken);
            var right = await second[index].GetValueAsync(TestContext.Current!.Execution.CancellationToken);
            await Assert.That(left != right).IsTrue();
        }
    }
}
