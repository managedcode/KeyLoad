using System.Text.Json;

namespace KeyLoad.UnitTests.Features.BenchmarkComparisons;

/// <summary>AC-VQ-007: closed original-file inventory includes every profile and resource sidecar.</summary>
internal sealed class CompositeSiteContractTests
{
    private const string PlansKey = "plans";
    private const string CellsKey = "cells";
    private const string SuiteKey = "suite";
    private const string ProviderKey = "provider";
    private const string UniqueSuiteKey = "uniqueSuite";
    private const string UniqueProviderKey = "uniqueProvider";
    private const string SidecarsKey = "sidecars";
    private const string RawKey = "raw";
    private const string VectorProfilesKey = "vectorProfiles";
    private const string ScalesKey = "scales";
    private const string HasFiveMillionKey = "hasFiveMillion";
    private const string OutsideKey = "outside";
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
        await Assert.That(result.GetProperty(PlansKey).GetInt32()).IsEqualTo(27);
        await Assert.That(result.GetProperty(CellsKey).GetInt32()).IsEqualTo(924);
        await Assert.That(result.GetProperty(SuiteKey).GetInt32()).IsEqualTo(1656);
        await Assert.That(result.GetProperty(ProviderKey).GetInt32()).IsEqualTo(60);
        await Assert.That(result.GetProperty(UniqueSuiteKey).GetInt32()).IsEqualTo(1656);
        await Assert.That(result.GetProperty(UniqueProviderKey).GetInt32()).IsEqualTo(60);
        await Assert.That(result.GetProperty(SidecarsKey).GetInt32()).IsEqualTo(704);
        await Assert.That(result.GetProperty(RawKey).GetInt32()).IsEqualTo(924);
        await Assert.That(result.GetProperty(VectorProfilesKey).GetInt32()).IsEqualTo(24);
        await Assert.That(result.GetProperty(ScalesKey).EnumerateArray().Select(item => item.GetInt32()))
            .IsEquivalentTo(new[] { 100_000, 1_000_000 }, TUnit.Assertions.Enums.CollectionOrdering.Matching);
        await Assert.That(result.GetProperty(HasFiveMillionKey).GetBoolean()).IsFalse();
        await Assert.That(result.GetProperty(OutsideKey).GetBoolean()).IsFalse();
    }
}
