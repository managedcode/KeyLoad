using System.Text.Json;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-VQ-007: closed original-file inventory includes every profile and resource sidecar.</summary>
internal sealed class CompositeSiteContractTests
{
    private const string Probe = """
        import {pathToFileURL} from 'node:url';
        const inventory=await import(pathToFileURL(process.argv[1]).href);
        const plans=inventory.compositeSitePlans();
        const suite=inventory.compositeSuiteFiles();
        const provider=inventory.compositeProviderFiles();
        const cells=inventory.compositeSiteCells();
        const result={plans:plans.length,cells:cells.length,suite:suite.length,provider:provider.length,
          uniqueSuite:new Set(suite).size,uniqueProvider:new Set(provider).size,
          sidecars:suite.filter(file=>file.endsWith('/server-resource-evidence.json')).length,
          raw:suite.filter(file=>file.endsWith('/worker.json')).length,
          vectorProfiles:plans.filter(plan=>plan.profile.startsWith('vector-')).length,
          scales:plans.filter(plan=>plan.profile.startsWith('scaled-')).map(plan=>plan.profileSettings.documents),
          hasFiveMillion:suite.some(file=>file.includes('-5m-')),outside:suite.some(file=>file.includes('..'))};
        process.stdout.write(JSON.stringify(result));
        """;

    [Test]
    public async Task OriginalInventoryContainsEveryControlScaleAndVectorCellWithoutDuplicatePaths()
    {
        var response = await IsolatedAggregateNodeProcess.RunAsync(["--input-type=module", "-e", Probe,
            IsolatedAggregateNodeProcess.Module("composite-site-contract.mjs")], TestContext.Current!.Execution.CancellationToken);
        await Assert.That(response.ExitCode).IsEqualTo(0).Because(response.Error);
        using var document = JsonDocument.Parse(response.Output);
        var result = document.RootElement;
        await Assert.That(result.GetProperty("plans").GetInt32()).IsEqualTo(27);
        await Assert.That(result.GetProperty("cells").GetInt32()).IsEqualTo(1386);
        await Assert.That(result.GetProperty("suite").GetInt32()).IsEqualTo(2470);
        await Assert.That(result.GetProperty("provider").GetInt32()).IsEqualTo(60);
        await Assert.That(result.GetProperty("uniqueSuite").GetInt32()).IsEqualTo(2470);
        await Assert.That(result.GetProperty("uniqueProvider").GetInt32()).IsEqualTo(60);
        await Assert.That(result.GetProperty("sidecars").GetInt32()).IsEqualTo(1056);
        await Assert.That(result.GetProperty("raw").GetInt32()).IsEqualTo(1386);
        await Assert.That(result.GetProperty("vectorProfiles").GetInt32()).IsEqualTo(24);
        await Assert.That(result.GetProperty("scales").EnumerateArray().Select(item => item.GetInt32()))
            .IsEquivalentTo(new[] { 100_000, 1_000_000 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(result.GetProperty("hasFiveMillion").GetBoolean()).IsFalse();
        await Assert.That(result.GetProperty("outside").GetBoolean()).IsFalse();
    }
}
